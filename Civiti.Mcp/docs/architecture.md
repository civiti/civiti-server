# Civiti.Mcp — Architecture Design

> **Status:** Living document. Section numbers stable; new sections append.
> Last revision: 2026-04-21 — updated for three-service topology
> (Api + Mcp + Auth); switched to standard 3-library Clean Architecture
> split (Domain / Application / Infrastructure); open questions
> consolidated into §10 decisions log.

## 1. Goals and non-goals

### Goals

- Let Civiti users and admins act on the platform from any MCP-compatible AI
  client, using natural-language conversations, without leaving that client.
- Reuse the existing Civiti business logic (validation, moderation,
  gamification, audit trails) so MCP actions are indistinguishable from
  equivalent REST actions from the domain's point of view.
- Keep the MCP surface small and curated — tools with clear semantics the
  model can use reliably, not a 1:1 mirror of every REST endpoint.

### Non-goals

- Replacing or deprecating the REST API. The Angular app and any mobile
  clients continue to talk to `Civiti.Api`.
- Letting LLMs bypass moderation, rate limits, or auth policy. MCP tool
  handlers call the same service methods the REST endpoints call.
- Supporting local/stdio MCP transport. Civiti is a multi-tenant hosted
  product; users will not run local binaries.

## 2. Deployment shape: three services

```
  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
  │  Civiti.Api  │   │  Civiti.Mcp  │   │  Civiti.Auth │
  │    (REST)    │   │  (MCP srvr)  │   │ (OAuth 2.1)  │
  └──────┬───────┘   └──────┬───────┘   └──────┬───────┘
         │                  │                  │
         └──────────────────┼──────────────────┘
                            ▼
         ┌─────────────────────────────────────────────┐
         │  Civiti.Application — services, DTOs,       │
         │    use-case coordination, validation        │
         │  Civiti.Domain — entities, value objects,   │
         │    enums, domain exceptions                 │
         │  Civiti.Infrastructure — EF Core +          │
         │    migrations, external clients, email,     │
         │    push, background-service implementations │
         └──────────────────────┬──────────────────────┘
                                ▼
         ┌─────────────────────────────────────────────┐
         │  PostgreSQL  ·  Supabase Auth               │
         └─────────────────────────────────────────────┘
```

**Three processes, one solution, one database, one shared domain/application layer.** All three hosts deploy as separate Railway services and reference the same three class libraries.

| Host | Role |
| --- | --- |
| `Civiti.Api` | REST backend. Serves the Angular app and future mobile clients. Continues to validate Supabase JWTs directly — unchanged auth path. |
| `Civiti.Mcp` | MCP Resource Server. Hosts `/mcp` (OAuth bearer required) and `/mcp/public` (anonymous). Validates tokens minted by Civiti.Auth. See [`tool-inventory.md`](tool-inventory.md). |
| `Civiti.Auth` | OAuth 2.1 Authorization Server (OpenIddict). Login delegation to Supabase, consent UI, token issuance and rotation, revocation, MCP-session lifecycle. See [`auth-design.md`](auth-design.md). |

### Two MCP transports

`Civiti.Mcp` exposes two endpoints on the same process:

- **`/mcp`** — OAuth 2.1 bearer required. Full tool surface, scoped by granted OAuth scope. See [`auth-design.md`](auth-design.md).
- **`/mcp/public`** — no auth. Mirrors the existing anonymous REST surface (issue browsing, authority directory, petition-counter bump). IP-rate-limited. See [`tool-inventory.md` §1](tool-inventory.md#1-public-anonymous-tools).

Both endpoints share the same tool handlers in `Civiti.Application`; the difference is whether `Civiti.Mcp` passes an authenticated `userId` or `null`. The public endpoint exists so an AI agent can help an unauthenticated visitor browse Civiti and draft a petition without being signed up — it is the MCP equivalent of visiting civiti.app anonymously.

### Why not embed MCP into Civiti.Api?

- MCP clients speak a different transport (Streamable HTTP / SSE). Mixing
  that into the REST `Program.cs` complicates both.
- AI agents fire tool calls in bursts; we want independent rate limits,
  pool sizing, and horizontal scaling so an MCP traffic spike can't saturate
  connections the Angular app depends on.
- Crash / deploy isolation: a bug in a new MCP tool should not page the REST
  service.

### Why not a separate repository?

- Shared domain drift is the single biggest risk. Keeping all hosts in the
  same solution, referencing the same class libraries, makes drift
  impossible by construction.
- Migrations, CI, and the test suite stay coherent.

## 3. <a id="library-extraction-prerequisite"></a>Library extraction (prerequisite PR)

Before `Civiti.Mcp` or `Civiti.Auth` can exist, `Civiti.Api` needs to stop
being a single monolithic project. The target is **standard .NET Clean
Architecture**: three class libraries with strict one-way dependencies, and
N host projects on top.

### Library layout

| Library | Contents | References |
| --- | --- | --- |
| `Civiti.Domain` | Entities (`Issue`, `UserProfile`, `Comment`, `Report`, `AdminAction`, `Authority`, …), value objects, enums (`IssueCategory`, `IssueStatus`, `UrgencyLevel`, …), domain exceptions, domain-level interfaces. Pure POCOs — no EF attributes, no DTOs. | *(none)* |
| `Civiti.Application` | Service interfaces and implementations (`IssueService`, `UserService`, `AdminService`, `GamificationService`, `ClaudeEnhancementService`, moderation, reporting, blocking), **DTOs / Requests / Responses**, validation, use-case coordination. | `Civiti.Domain` |
| `Civiti.Infrastructure` | EF Core `CivitiDbContext` + `Data/Configurations/**` + `Migrations/**`, external clients (Anthropic, OpenAI moderation, Supabase Admin, Resend, Expo push), email templates, push-token management, concrete implementations of background services declared in `Civiti.Application`. | `Civiti.Application` + `Civiti.Domain` |

### Host projects (post-refactor)

| Host | Contents | References |
| --- | --- | --- |
| `Civiti.Api` | `Program.cs`, `Endpoints/**`, Swagger examples, REST-specific middleware. Shrunk from today's monolith. | `Civiti.Application` + `Civiti.Infrastructure` |
| `Civiti.Mcp` *(new)* | `Program.cs`, MCP tool handlers, both transports (`/mcp` + `/mcp/public`), OpenIddict Validation setup, protected-resource metadata endpoint. | `Civiti.Application` + `Civiti.Infrastructure` |
| `Civiti.Auth` *(new)* | `Program.cs`, OpenIddict Server setup, consent Razor Pages, Supabase login delegation, `McpSessions` management, revocation endpoints. | `Civiti.Application` + `Civiti.Infrastructure` |

### Rules for the extraction PR

1. **No behavior changes.** Pure `git mv` + namespace rewrites + csproj reference updates.
2. **All existing tests pass unchanged.**
3. **Civiti.Api remains the only host.** The refactor PR does *not* add Civiti.Mcp or Civiti.Auth. It only prepares the ground by splitting the libraries.
4. **Civiti.Api remains the sole migration runner.** No other host touches migrations unless explicitly redesigned later.
5. **ASP.NET-specific code stays in each host.** Error-handling middleware, Serilog enrichers, and similar live in each host project until we see enough duplication to justify a `Civiti.Web` shared library. **Don't create `Civiti.Web` speculatively.**

### Background services per host (post-refactor)

Background service *implementations* live in `Civiti.Infrastructure` as plain classes. Each host decides which to register as hosted services at startup:

| Background service | Civiti.Api | Civiti.Mcp | Civiti.Auth |
| --- | --- | --- | --- |
| `EmailSenderBackgroundService` | ✓ | — | — |
| `PushNotificationSenderBackgroundService` | ✓ | — | — |
| `AdminNotifyBackgroundService` | ✓ | — | — |
| `StaticDataSeeder` | ✓ | — | — |
| `JwksBackgroundService` (Supabase JWKS cache) | ✓ | — | ✓ *(used during login handoff)* |
| Migration runner | ✓ | — | — |
| MCP-session role-revalidation sweep *(new)* | — | — | ✓ |

Civiti.Mcp runs zero hosted services: OpenIddict Validation's JWKS cache for Civiti.Auth tokens is in-process and doesn't need a separate worker.

## 4. Transport

MCP supports two remote transports: the older HTTP+SSE pair and the newer **Streamable HTTP** (single endpoint, optional SSE upgrade). We use Streamable HTTP:

- It is the current MCP spec's recommended transport.
- Anthropic's `ModelContextProtocol.AspNetCore` NuGet package has first-class support: `app.MapMcp("/mcp")`.
- A single endpoint simplifies OAuth protected-resource metadata and routing.

## 5. Hosting, config, and process model

- **Runtime:** .NET 10, ASP.NET Core Minimal Host (same as `Civiti.Api`).
- **Entry point:** `Civiti.Mcp/Program.cs`.
- **SDK:** `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (official Anthropic C# SDK).
- **Config:** Same precedence as `Civiti.Api` (env vars → `appsettings.json` → exception). Shares `DATABASE_URL`, `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEY`, `SUPABASE_SERVICE_KEY`. Adds new vars documented in [`auth-design.md`](auth-design.md).
- **Port:** Reads Railway `PORT` env var like `Civiti.Api`.
- **Database connection pool:** Independent from `Civiti.Api`. Size it smaller initially — MCP traffic will be lower volume but burstier per session.

Civiti.Auth follows the same runtime choices; its specifics are documented in [`auth-design.md`](auth-design.md).

## 6. Observability

- **Logging:** Same Serilog pipeline and sinks as `Civiti.Api`. Every MCP request gets a correlation id; every tool invocation logs `{tool_name, session_id, user_id, duration_ms, outcome}` (session_id and user_id are null for anonymous `/mcp/public` calls).
- **Audit trail:** Write operations stamp their audit record with `Source = "mcp"`. `AdminAction` already has a notes field; add a structured `source` column in a follow-up migration so we can filter "approvals via MCP vs. via admin UI" in dashboards.
- **Metrics:** Expose `/api/health` (DB + Supabase check) the same way `Civiti.Api` does. Add tool-level counters later.

## 7. Rate limiting and abuse

- **Per-MCP-session** sliding-window limit on total tool invocations (authenticated `/mcp` only).
- **Per-source-IP** limits on anonymous `/mcp/public`.
- **Per-tool class** limits — see [`tool-inventory.md`](tool-inventory.md#rate-limit-classes) for the concrete table (`read.public`, `read.cheap`, `read.search`, `write.citizen`, `write.admin`, `ai.claude`).
- **User-wide** limit that sums across sessions, so the same user opening five clients doesn't 5× the budget.
- **Content moderation** on every tool input that stores user-generated text — delegate to the same OpenAI moderation pipeline `Civiti.Api` uses for `create_issue` / `comment`.

## 8. What MCP clients actually get

Three kinds of capability:

- **Tools:** Named functions with JSON schemas (see `tool-inventory.md`).
- **Resources:** URI-addressable read-only data (`civiti://issue/{id}`, `civiti://authority/{city}`, etc.). These let agents cite and reason over Civiti data without spending tool calls.
- **Prompts:** Reusable templates the client can surface to the user. Optional; add after tools are stable.

## 9. Open questions

1. **Cost control for `draft_issue_description`.** Calls Claude; costs money. Leaning toward reusing the existing `ClaudeEnhancementService` rate limiter (10 req/min/user, shared with REST `/enhance-text`). Formalize as a decision once we've observed real MCP traffic patterns — if agents hammer it in a loop, we may need a tighter MCP-specific budget.

## 10. Resolved decisions (log)

- **2026-04-21** — **Three-service topology (Api + Mcp + Auth).** Auth split out per auth-design.md, for standard OAuth 2.1 RS/AS separation and future reuse.
- **2026-04-21** — **Standard 3-library Clean Architecture split: Civiti.Domain / Civiti.Application / Civiti.Infrastructure.** (Supersedes an earlier 4-library proposal with separate `Civiti.Data` and `Civiti.Services`.) Rationale: Clean Architecture is the .NET ecosystem default; fewer csproj reference edges reduces extraction fragility; DTOs belong in Application, not Domain; EF + migrations belong in Infrastructure, not a separate Data project.
- **2026-04-21** — **"Application" naming over "Services".** Aligns with Clean Architecture convention; avoids collision with ASP.NET's "hosted services" terminology.
- **2026-04-21** — **Admin access via MCP is in scope, gated.** See [`auth-design.md` §9](auth-design.md#9-admin-specific-hardening) and [`tool-inventory.md` §3](tool-inventory.md#3-admin-tools). Opt-in flag + two-step confirm + stricter rate limits + Verified/Unverified trust badge on consent.
- **2026-04-21** — **Anonymous read path in scope** via dedicated `/mcp/public` endpoint. See [`tool-inventory.md` §1](tool-inventory.md#1-public-anonymous-tools).
- **2026-04-21** — **Civiti.Api remains the sole migration runner.** No other host invokes migrations. Revisit only if deployment topology forces otherwise (e.g. dedicated migration job).
- **2026-04-21** — **No speculative `Civiti.Web` shared library.** ASP.NET-specific code (error-handling middleware, Serilog enrichers) stays duplicated across hosts until duplication pain justifies extraction. YAGNI.
