# Shared-library extraction — refactor plan

> **Status:** Draft for review. Executes the library-extraction step defined in
> [`Civiti.Mcp/docs/architecture.md §3`](../../Civiti.Mcp/docs/architecture.md#3-library-extraction-prerequisite).
>
> **Scope:** Pure mechanical extraction of `Civiti.Api` into three class
> libraries (`Civiti.Domain`, `Civiti.Application`, `Civiti.Infrastructure`)
> plus a shrunk `Civiti.Api` host. **No behaviour changes.** All existing
> tests must pass unchanged.
>
> This plan is the artifact reviewers sign off *before* the mechanical PR
> is opened, so the diff there is "plan + git mv + namespace rewrites,
> nothing else".

## 1. Target topology

```
solution
├── Civiti.Domain            ← entities, enums, domain errors, localization,
│                              value objects. Pure POCOs. No dependencies.
├── Civiti.Application       ← service interfaces + DTOs / Requests /
│                              Responses / Notifications. No concrete
│                              service implementations (see §12 decision
│                              log — every service injects DbContext or
│                              an SDK, so the impls all belong in
│                              Infrastructure).
│                              Depends on: Civiti.Domain.
├── Civiti.Infrastructure    ← EF DbContext + configurations + migrations,
│                              **all service implementations** (both
│                              business-logic and external-client —
│                              they share DbContext coupling), external
│                              adapters (Anthropic, OpenAI, Supabase
│                              Admin, Resend, Expo, QuestPDF, JWKS cache),
│                              email templates.
│                              Depends on: Civiti.Application + Civiti.Domain.
├── Civiti.Api               ← shrunk HTTP host. Program.cs, Endpoints/**,
│                              HTTP-specific middleware, Swagger + JWT
│                              config, route constants, claims extensions.
│                              Depends on: Civiti.Application + Civiti.Infrastructure.
└── Civiti.Tests             ← references shift from Civiti.Api → the three
                               shared libraries. No test-logic changes.
```

## 2. File-move table

Counts reflect the inventory of the current `Civiti.Api/` tree at the tip of
master; tally before executing to confirm nothing drifts.

### → `Civiti.Domain`

| From `Civiti.Api/` | To `Civiti.Domain/` | Count | Notes |
| --- | --- | --- | --- |
| `Models/Domain/**` | `Entities/**` | 19 | All entities are pure POCOs — zero EF attributes. Move as-is. |
| `Infrastructure/Localization/{Achievement,Badge,Poster}Localization.cs` | `Localization/**` | 3 | Pure domain label tables. `CategoryLocalization` is excluded from this move because it references `CategoryResponse` (a Response DTO that moves to Civiti.Application in commit 3) — letting it land in Civiti.Domain would require `Domain → Application`, which inverts the dependency arrow. It moves to `Civiti.Application/Localization/` in commit 3 alongside `CategoryResponse`. See the §12 decision logged against this split. |
| `Infrastructure/Exceptions/AccountDeletedException.cs` | `Exceptions/AccountDeletedException.cs` | 1 | Domain-level exception. |
| `Infrastructure/Constants/DomainErrors.cs` | `Constants/DomainErrors.cs` | 1 | Domain error code table. |
| `Infrastructure/Constants/ReportTargetTypes.cs` | `Constants/ReportTargetTypes.cs` | 1 | Stable enum-like constants. |
| `Infrastructure/Constants/IssueValidationLimits.cs` | `Constants/IssueValidationLimits.cs` | 1 | Domain validation limits referenced by both services and endpoints. |

### → `Civiti.Application`

| From `Civiti.Api/` | To `Civiti.Application/` | Count | Notes |
| --- | --- | --- | --- |
| `Models/Requests/**` | `Requests/**` | 7 | Request records, grouped by feature folder (`Activity`, `Admin`, `Auth`, `Comments`, `Issues`, `Push`, `Reports`). |
| `Infrastructure/Extensions/SignupMetadata.cs` | `Requests/Auth/SignupMetadata.cs` | 1 | Pure data record (Supabase sign-up metadata). Referenced by `IUserService` — an Application contract — so it must live in Civiti.Application despite being parsed from a JWT by `ClaimsPrincipalExtensions` (which stays in Civiti.Api and picks up a `using Civiti.Application.Requests.Auth;`). Originally classified under "Stays in Civiti.Api"; corrected during commit-3 execution. |
| `Models/Responses/**` | `Responses/**` | 13 | Response records, grouped by feature folder. |
| `Models/Notifications/**` | `Notifications/**` | 1 | `AdminNotifyRequest`. |
| `Models/Email/**` | `Email/Models/**` | 1 | `EmailNotification` payload type. Templates go to Infrastructure. |
| `Models/Push/**` | `Push/Models/**` | 1 | `PushNotificationMessage` payload type. |
| `Services/Interfaces/**` | `Services/**` | 21 | **All** service interfaces — both the ones whose implementations stay DbContext-free and the ones whose implementations use external clients. Application is the layer where every service *contract* lives; every concrete implementation lives in Infrastructure (see below and §12). |

There is no `Models/DTOs/` folder in the current tree; request/response records under `Models/Requests/**` and `Models/Responses/**` fill the DTO role. The §3 rewrite table reflects this — no `Civiti.Api.Models.DTOs` namespace exists to rewrite.

**No concrete `*Service.cs` classes land in `Civiti.Application`.** Every service today injects either `CivitiDbContext` (an Infrastructure type) or an external SDK client. With no repository pattern in place and no plan to introduce one (§10), every implementation is Infrastructure by Clean Architecture's definition — the interface is the layer boundary.

### → `Civiti.Infrastructure`

| From `Civiti.Api/` | To `Civiti.Infrastructure/` | Count | Notes |
| --- | --- | --- | --- |
> Interface files (`Services/Interfaces/**`) for every row below move to `Civiti.Application` (see that section). Only the `*.cs` implementation files move to `Civiti.Infrastructure`.

| `Data/CivitiDbContext.cs` | `Data/CivitiDbContext.cs` | 1 | |
| `Data/Configurations/**` | `Data/Configurations/**` | 18 | Fluent EF configurations. |
| `Migrations/**` | `Migrations/**` | 15 | |
| `Services/ActivityService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/AdminService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/AuthService.cs` | `Services/**` | 1 | Currently an empty stub; lands here for consistency — will gain DbContext coupling as it's fleshed out. |
| `Services/AuthorityService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/BlockService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/CommentService.cs` | `Services/**` | 1 | Injects `CivitiDbContext` and `IContentModerationService`. |
| `Services/GamificationService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/IssueService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/NotificationService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/PushTokenService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/ReportService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/UserService.cs` | `Services/**` | 1 | Injects `CivitiDbContext`. |
| `Services/StaticDataSeeder.cs` | `Services/**` | 1 | Injects `CivitiDbContext`; seed-focused but infrastructure by coupling. |
| `Services/ClaudeEnhancementService.cs` | `Services/Claude/**` | 1 | Anthropic SDK client. |
| `Services/OpenAIModerationService.cs` | `Services/Moderation/**` | 1 | OpenAI SDK client. Interface `IContentModerationService` is consumed by `CommentService` — goes to Application. |
| `Services/SupabaseService.cs` | `Services/Supabase/**` | 1 | Supabase SDK client. |
| `Services/SupabaseAdminClient.cs` | `Services/Supabase/**` | 1 | Supabase Admin API. |
| `Services/EmailSenderService.cs` | `Services/Email/**` | 1 | Resend SMTP client. |
| `Services/EmailSenderBackgroundService.cs` | `Services/Email/**` | 1 | `IHostedService` implementation. |
| `Services/EmailTemplateService.cs` | `Services/Email/**` | 1 | Template rendering. |
| `Services/PushNotificationSenderBackgroundService.cs` | `Services/Push/**` | 1 | Expo push sender. |
| `Services/PosterService.cs` | `Services/Poster/**` | 1 | QuestPDF + QRCoder. |
| `Services/JwksManager.cs` | `Services/Jwks/**` | 1 | Supabase JWKS cache. |
| `Services/JwksBackgroundService.cs` | `Services/Jwks/**` | 1 | `IHostedService`. |
| `Services/AdminNotifier.cs` | `Services/AdminNotify/**` | 1 | Channels-based in-process pub/sub; lives here because it couples to email/push infrastructure. |
| `Services/AdminNotifyBackgroundService.cs` | `Services/AdminNotify/**` | 1 | `IHostedService`; injects `CivitiDbContext`. |
| `Infrastructure/Email/**` | `Email/Templates/**` | 3 | `EmailLayout`, `EmailTemplates`, `EmailDataKeys`. |
| `Infrastructure/Configuration/AdminNotifyConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/ClaudeConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/ExpoPushConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/OpenAIConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/PosterConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/ResendConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/SupabaseConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/JwtValidationOptions.cs` | `Configuration/JwtValidationOptions.cs` | 1 | Consumed by `JwksManager` + `JwksBackgroundService` (both Infrastructure). Originally classified as "Stays in Civiti.Api" — corrected during commit-4 execution. `JwtBearerPostConfigureOptions.cs` stays because it's referenced only by Civiti.Api's `Program.cs`. |

### Stays in `Civiti.Api` (shrunk host)

| File / directory | Count | Notes |
| --- | --- | --- |
| `Program.cs` | 1 | DI registration updated to reference the new libraries; no behavioural change. |
| `appsettings*.json`, `.env` | 4 | |
| `Properties/`, `wwwroot/` | — | |
| `Endpoints/**` | 14 | Route groups. |
| `Infrastructure/Middleware/**` | 2 | `ErrorHandlingMiddleware`, `RequestLoggingMiddleware` — HTTP-specific. |
| `Infrastructure/Configuration/SwaggerConfiguration.cs` | 1 | |
| `Infrastructure/Configuration/SwaggerExamples.cs` | 1 | |
| `Infrastructure/Configuration/JwtBearerPostConfigureOptions.cs` | 1 | |
| `Infrastructure/Constants/ApiRoutes.cs` | 1 | HTTP routes. |
| `Infrastructure/Constants/AuthorizationPolicies.cs` | 1 | Policy-name constants; tied to the JWT policies registered in `Program.cs`. |
| `Infrastructure/Extensions/ClaimsPrincipalExtensions.cs` | 1 | JWT-claim helpers. |
| `Infrastructure/Extensions/JwtBearerExtensions.cs` | 1 | |

## 3. Namespace rewrite rules

Pure mechanical regex. Apply across every moved file.

| Pattern (old) | Replacement (new) | Notes |
| --- | --- | --- |
| `Civiti\.Api\.Models\.Domain` | `Civiti.Domain.Entities` |
| `Civiti\.Api\.Infrastructure\.Localization` | `Civiti.Domain.Localization` |
| `Civiti\.Api\.Infrastructure\.Exceptions` | `Civiti.Domain.Exceptions` |
| `Civiti\.Api\.Infrastructure\.Constants\.(DomainErrors|ReportTargetTypes|IssueValidationLimits)` | `Civiti.Domain.Constants.$1` |
| `Civiti\.Api\.Models\.Requests` | `Civiti.Application.Requests` |
| `Civiti\.Api\.Models\.Responses` | `Civiti.Application.Responses` |
| `Civiti\.Api\.Models\.Notifications` | `Civiti.Application.Notifications` |
| `Civiti\.Api\.Models\.Email` | `Civiti.Application.Email.Models` |
| `Civiti\.Api\.Models\.Push` | `Civiti.Application.Push.Models` |
| `Civiti\.Api\.Services\.Interfaces` | `Civiti.Application.Services` | (All interfaces land in Application, full stop.) |
| `Civiti\.Api\.Services\.(ActivityService\|AdminService\|AuthService\|AuthorityService\|BlockService\|CommentService\|GamificationService\|IssueService\|NotificationService\|PushTokenService\|ReportService\|UserService\|StaticDataSeeder)` | `Civiti.Infrastructure.Services.$1` | DbContext-coupled services. |
| `Civiti\.Api\.Services\.(ClaudeEnhancementService)` | `Civiti.Infrastructure.Services.Claude.$1` | |
| `Civiti\.Api\.Services\.(OpenAIModerationService)` | `Civiti.Infrastructure.Services.Moderation.$1` | |
| `Civiti\.Api\.Services\.(SupabaseService\|SupabaseAdminClient)` | `Civiti.Infrastructure.Services.Supabase.$1` | |
| `Civiti\.Api\.Services\.(EmailSenderService\|EmailSenderBackgroundService\|EmailTemplateService)` | `Civiti.Infrastructure.Services.Email.$1` | |
| `Civiti\.Api\.Services\.(PushNotificationSenderBackgroundService)` | `Civiti.Infrastructure.Services.Push.$1` | |
| `Civiti\.Api\.Services\.(PosterService)` | `Civiti.Infrastructure.Services.Poster.$1` | |
| `Civiti\.Api\.Services\.(JwksManager\|JwksBackgroundService)` | `Civiti.Infrastructure.Services.Jwks.$1` | |
| `Civiti\.Api\.Services\.(AdminNotifier\|AdminNotifyBackgroundService)` | `Civiti.Infrastructure.Services.AdminNotify.$1` | |
| `Civiti\.Api\.Data` | `Civiti.Infrastructure.Data` |
| `Civiti\.Api\.Migrations` | `Civiti.Infrastructure.Migrations` |
| `Civiti\.Api\.Infrastructure\.Configuration\.(AdminNotify\|Claude\|ExpoPush\|OpenAI\|Poster\|Resend\|Supabase)Configuration` | `Civiti.Infrastructure.Configuration.$1Configuration` |
| `Civiti\.Api\.Infrastructure\.Email` | `Civiti.Infrastructure.Email.Templates` |

Everything that stays in `Civiti.Api` keeps its current namespace. No rename needed for `Endpoints`, `Middleware`, `ClaimsPrincipalExtensions`, etc.

## 4. csproj reference graph

```
Civiti.Domain          → (no project refs)
Civiti.Application     → Civiti.Domain
Civiti.Infrastructure  → Civiti.Application, Civiti.Domain
Civiti.Api             → Civiti.Application, Civiti.Infrastructure
Civiti.Tests           → Civiti.Domain, Civiti.Application, Civiti.Infrastructure, Civiti.Api
```

Rationale for Civiti.Tests: existing tests target services (Infrastructure), entities + localization (Domain), validators + request records (Application), **and middleware** (Civiti.Api). Confirmed: `Civiti.Tests/Middleware/ErrorHandlingMiddlewareTests.cs` and `RequestLoggingMiddlewareTests.cs` both `using Civiti.Api.Infrastructure.Middleware;` — those middleware classes stay in Civiti.Api per §2, so Civiti.Tests keeps its `ProjectReference` to Civiti.Api. The reference graph grows, not shrinks.

## 5. NuGet package split

| Package | Current (Civiti.Api) | → Civiti.Domain | → Civiti.Application | → Civiti.Infrastructure | → Civiti.Api (stays) |
| --- | :-: | :-: | :-: | :-: | :-: |
| `Anthropic.SDK` | ✓ | | | ✓ | |
| `OpenAI` | ✓ | | | ✓ | |
| `Supabase` | ✓ | | | ✓ | |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | ✓ | | | ✓ | — (transitive via Infrastructure; also transitively pulls `Microsoft.EntityFrameworkCore` runtime — no separate runtime PackageReference exists today or is needed after the split) |
| `Microsoft.EntityFrameworkCore.Design` (tooling) | ✓ | | | | ✓ (must stay — `dotnet ef` CLI requires it in the startup project) |
| `Microsoft.EntityFrameworkCore.Tools` (tooling) | ✓ | | | | ✓ (same rationale as `.Design`; tooling-only, `PrivateAssets=all`) |
| `Resend` | ✓ | | | ✓ | |
| `QRCoder` | ✓ | | | ✓ | |
| `QuestPDF` | ✓ | | | ✓ | |
| `Serilog` (+ sinks) | ✓ | | | ✓ | ✓ (for host config) |
| `Swashbuckle.AspNetCore*` | ✓ | | | | ✓ |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | ✓ | | | | ✓ |
| `Microsoft.IdentityModel.Tokens` | ✓ | | ✓ | ✓ | ✓ |
| `System.IdentityModel.Tokens.Jwt` | ✓ | | | | ✓ |

`Civiti.Domain` takes zero NuGet packages. `Civiti.Application` takes exactly one — `Microsoft.IdentityModel.Tokens`, because `IJwksManager`'s method signatures expose `JsonWebKey`, `JsonWebKeySet`, and `SecurityKey` from that package directly. Hiding those types behind a neutral Application-layer abstraction would be a useful follow-up, but it's a behaviour-adjacent change out of scope for this mechanical refactor. The package also lands in `Civiti.Infrastructure` (for `JwksManager`'s implementation) and stays in `Civiti.Api` (for `JwtBearer` host config). Logged in §12. If any other validator or service turns out to need a small helper package, add it at that time with a note in the PR.

## 6. Phase order (one PR, staged commits)

Single PR, multiple commits, each one compilable and test-green. This gives us bisectability without multiple round-trips through review.

| # | Commit | What |
| --- | --- | --- |
| 1 | `chore: add empty Civiti.Domain / Civiti.Application / Civiti.Infrastructure projects to solution` | Three empty csprojs, updated `.sln`, no code movement. CI green trivially. |
| 2 | `refactor: move entities and domain constants to Civiti.Domain` | Execute the Domain move + namespace rewrite. Civiti.Api picks up ProjectReference to Civiti.Domain. Tests pass. |
| 3 | `refactor: move DTOs and service interfaces to Civiti.Application` | Execute the Application move — DTOs, Requests, Responses, Notifications, Email/Push models, and all 21 service interfaces. **No service implementations move in this commit.** Civiti.Api picks up ProjectReference to Civiti.Application. Tests pass. |
| 4 | `refactor: move EF DbContext, migrations, all service implementations, and external clients to Civiti.Infrastructure` | Execute the Infrastructure move. This commit is the largest — it carries the DbContext, 15 migrations, 18 fluent configurations, all 26 service implementations (business-logic + external-client alike), email templates, and external configuration classes. Migrate NuGet packages. Civiti.Api picks up ProjectReference to Civiti.Infrastructure; drops the packages that moved. Tests pass. |
| 5 | `refactor: shrink Civiti.Api and update Civiti.Tests references` | Clean up any straggler `using` statements in Civiti.Api, update Civiti.Tests' ProjectReferences, delete the now-empty `Civiti.Api/Models/`, `Civiti.Api/Data/`, `Civiti.Api/Services/`, `Civiti.Api/Migrations/` folders. Tests pass. |

At each commit, the build is green and `dotnet test` passes. Use `git bisect` if something breaks later.

## 7. Definition of done

- [ ] `dotnet build` succeeds with zero warnings new to this PR.
- [ ] `dotnet test` passes — same test count, same behaviour.
- [ ] `dotnet ef migrations list` from `Civiti.Api` (referencing Civiti.Infrastructure's DbContext) lists the same migrations as before.
- [ ] `dotnet run` against a fresh Postgres applies migrations and boots to a healthy `/api/health`.
- [ ] Swagger UI still renders and all pre-existing endpoints are visible.
- [ ] No existing endpoint's behaviour changes (spot-check: one authed POST, one anonymous GET, one admin POST).
- [ ] The Railway deploy (staging) completes migrations and boots.
- [ ] Every file moved has had its namespace updated; no orphan `using Civiti.Api.Models.Domain;` statements remain anywhere.
- [ ] `Civiti.Api.csproj` no longer references NuGet packages that moved: Anthropic.SDK, OpenAI, Supabase, `Npgsql.EntityFrameworkCore.PostgreSQL`, Resend, QRCoder, QuestPDF. **`Microsoft.EntityFrameworkCore.Design` and `Microsoft.EntityFrameworkCore.Tools` must remain** in `Civiti.Api.csproj` — EF CLI tooling (`dotnet ef migrations list|add|update`) requires them in the designated startup project, and the very DoD item above relies on it. Both are `PrivateAssets=all`, so they don't leak into transitive consumers.

## 8. Risks and mitigations

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| EF migrations break because `CivitiDbContext` is in a different assembly | Medium | Use `dotnet ef migrations list` and a full local DB reset before opening the PR. EF Core handles cross-assembly contexts via `MigrationsAssembly()` — set it explicitly on the context's registration in Civiti.Api's `Program.cs`. |
| Tests fail on a missing `InternalsVisibleTo` | Addressed by plan | `Civiti.Api.csproj` currently declares `<InternalsVisibleTo Include="Civiti.Tests" />`, and `Civiti.Tests/Services/SupabaseAdminClientTests.cs` reaches `SupabaseAdminClient.UsersPage` and `SupabaseAdminClient.ParsedUser` (both `internal sealed record`). When `SupabaseAdminClient.cs` moves to `Civiti.Infrastructure` in commit 4, the `InternalsVisibleTo` attribute moves to `Civiti.Infrastructure.csproj`. `Civiti.Api.csproj` keeps its own attribute for any future `internal` types it retains (middleware, extensions). No other test reaches `internal` members today — grep before commit 4 to confirm no new drift. |
| Generic-host DI ordering changes | Low | `Program.cs` is the only DI registration point, and it's staying in Civiti.Api. Registration logic doesn't move. |
| Serilog configuration breaks | Low | Sink setup stays in `Program.cs`; service-layer `ILogger<T>` injection works identically. |
| Namespace collisions after rewrite (e.g., two `Services` folders under different top-level namespaces both claim the same short name) | Low | Rewrite script is fully-qualified. Build will fail loudly on collision; no silent drift possible. |
| Circular project references (e.g., Civiti.Domain accidentally references Civiti.Application) | Medium | Enforce via csproj review — Civiti.Domain has zero `<ProjectReference>`. A CI step adding `<PackageReference>` and `<ProjectReference>` validation is out of scope; the reviewer is the gate. |
| A service implementation classified as Application-layer ends up needing an Infrastructure type (e.g. `CivitiDbContext`, an SDK client), which would require `Application → Infrastructure` and invert the dependency arrow | Caught in review | Resolved at plan-stage (Greptile PR #83 review): **no service implementations live in Application**; only interfaces + DTOs do. The execution PR's reviewer should spot-check the Application project for any accidentally-added `*Service.cs` file. |
| `Infrastructure/Constants/IssueValidationLimits` used both from endpoints (Civiti.Api) and services (Civiti.Infrastructure) creates a Domain-is-referenced-by-both case | None (works) | That's exactly what putting it in Civiti.Domain is for — both referencing projects already depend on Domain. |

## 9. Rollback plan

Each commit in the PR is green. If a post-merge problem surfaces:

- **Same-PR breakage during review:** `git revert` the offending commit; the series remains valid.
- **Post-merge breakage in `Civiti.Api`:** `git revert` the merge commit on master; the revert is safe because no behaviour changed. Re-open the PR for whatever single-commit fix is needed.
- **Partial rollback:** not meaningful — the four libraries are interdependent by design. Treat the refactor as one unit.

Because the PR adds no behaviour and no migrations, there is no data-shape risk to worry about.

## 10. Out of scope

Explicitly **not** part of this PR:

- Any new feature or endpoint.
- Renaming anything outside the top-level namespace change (e.g. method renames, file re-grouping within a library beyond what §2 specifies).
- Introducing FluentValidation, MediatR, AutoMapper, or any other new library.
- **Introducing a repository pattern** (`IIssueRepository`, etc.) to decouple service implementations from `CivitiDbContext`. This is why every service implementation lands in Infrastructure rather than Application — see §12. A later refactor can add repositories if we decide the Application layer should hold concrete services; until then, "interfaces in Application, impls in Infrastructure" is the correct Clean-Architecture split.
- Changing Program.cs's DI registration structure beyond swapping `using` imports.
- Adding `<InternalsVisibleTo>` or `<AssemblyAttribute>` anywhere.
- Creating Civiti.Auth or Civiti.Mcp host projects.

These are follow-up PRs; each has its own design doc in `Civiti.Mcp/docs/`.

## 11. Review guide

Read in this order:

1. §1 topology and §4 reference graph — the mental model.
2. §6 phase order — how the diff will be structured.
3. §2 file-move table — the authoritative list. Challenge any row whose destination feels wrong.
4. §3 namespace rewrites — confirm regex patterns are total and unambiguous.
5. §5 package split — confirm no package ends up in two libraries.
6. §7 done-definition — the PR's exit criteria.
7. §8 and §9 — risks and escape hatches.

Approve this plan before the mechanical PR is opened. The review on the mechanical PR will then be "diff matches plan", not "is this the right plan?".

## 12. Resolved decisions (log)

- **2026-04-22** — Single PR, five staged commits (one per library + cleanup). Rejected alternative: one PR per library. The four libraries are interdependent enough that a partial merge would leave master in an awkward state; bisectable-commits-in-one-PR gives us review granularity without exposing master to intermediate states.
- **2026-04-22** — `Models/DTOs|Requests|Responses|Notifications|Email|Push` all move to Civiti.Application. Rejected alternative: DTOs in Civiti.Domain. DTOs are application-layer contracts, not domain truths; keeping Domain pure of DTOs is the standard Clean Architecture shape and prevents the library from accidentally growing serialization attributes later.
- **2026-04-22** — `IssueValidationLimits`, `DomainErrors`, `ReportTargetTypes` land in Civiti.Domain. Rejected alternative: keep in Civiti.Api or Civiti.Application. They are stable facts about the domain that both HTTP endpoints and services reference; Domain is where they belong.
- **2026-04-22** — `NotificationService` (in-process) is classified as Infrastructure despite having no external deps, because it lives alongside `AdminNotifier` in the notification-coordination stack. Revisit during implementation if test coverage benefits from moving it to Application. *(Moot post-Greptile review: `NotificationService.cs` injects `CivitiDbContext` too, so it would land in Infrastructure regardless — see the decision below.)*
- **2026-04-22** — **All service implementations live in Civiti.Infrastructure; Civiti.Application holds only service interfaces + DTOs + payload types.** (Supersedes an earlier pass of this plan which classified 10 "business-logic" services as Application.) Rationale: every service in the current `Civiti.Api/Services/` directory injects `CivitiDbContext` directly (confirmed by grep against all service files). Because `CivitiDbContext` lives in Infrastructure, any implementation that depends on it also lives in Infrastructure — otherwise we'd need `Application → Infrastructure`, which inverts Clean Architecture's dependency direction. A repository pattern could break this coupling and allow impls in Application, but introducing one is explicitly out of scope (§10). The resulting split matches the textbook Clean Architecture shape when direct DbContext access is used: Application is the contract layer (interfaces + DTOs only); Infrastructure owns every class that concretely touches a framework or external system. Caught by Greptile review on PR #83.
- **2026-04-22** — **`IContentModerationService` interface lives in Civiti.Application**, not Infrastructure. The concrete `OpenAIModerationService` is Infrastructure (OpenAI SDK client), but the interface is an application-layer contract consumed by other services (notably `CommentService`). This is the general rule: every service interface moves to Application regardless of where its implementation lives. Caught by Greptile review on PR #83.
- **2026-04-22** — **`Microsoft.IdentityModel.Tokens` is added to `Civiti.Application` (and kept in `Civiti.Infrastructure` + `Civiti.Api`)**, superseding §5's earlier "Civiti.Application takes zero NuGet packages" rule. Caught during commit-3 execution: `IJwksManager` exposes `JsonWebKey`, `JsonWebKeySet`, and `SecurityKey` in its method signatures. Those types live in `Microsoft.IdentityModel.Tokens`; the interface won't compile without it. Alternatives — (a) keep `IJwksManager` in Civiti.Api, which would be the only interface doing so and would contradict §12's "every service interface lives in Civiti.Application" rule, or (b) reshape the interface to hide the package types behind a neutral abstraction, which is behaviour-adjacent and out of scope for a mechanical refactor (§1). Adding the package is the smallest legal move. A later refactor can pull the types behind a domain abstraction if we want Application back to zero packages.
- **2026-04-22** — **`SignupMetadata` moves to `Civiti.Application/Requests/Auth/`, not "stays in Civiti.Api"** (supersedes §2's earlier placement). Caught during commit-3 execution: `IUserService.GetOrCreateUserProfileAsync(..., SignupMetadata? ...)` — an Application-layer contract — requires `SignupMetadata`. The type itself is a pure record with no HTTP dependencies; the original "HTTP-request-bound" classification referred to where it's *consumed* (JWT parsing in `ClaimsPrincipalExtensions`), not what it *is*. Letting it stay in Civiti.Api would require `Application → Api`, inverting the dependency arrow. `ClaimsPrincipalExtensions.cs` stays in Civiti.Api and picks up a `using Civiti.Application.Requests.Auth;` to consume the moved type.
- **2026-04-22** — **`CategoryLocalization` moves to `Civiti.Application/Localization/` in commit 3, not to `Civiti.Domain/Localization/` in commit 2** (supersedes §2's earlier "all 4 localization files → Civiti.Domain" row). Caught during commit-2 execution: `CategoryLocalization.GetAll()` returns `List<CategoryResponse>` where `CategoryResponse` is a Response DTO that moves to `Civiti.Application.Responses.Common` in commit 3. Putting `CategoryLocalization` in Civiti.Domain would force `Civiti.Domain → Civiti.Application`, inverting the dependency arrow. The cleanest split is to classify `CategoryLocalization` as application-layer (it's a localization-of-DTO concern, not a pure domain fact), the same way `IContentModerationService`'s interface is in Application even though its implementation is Infrastructure. The other three localization files (`AchievementLocalization`, `BadgeLocalization`, `PosterLocalization`) are pure domain label tables and still move to Civiti.Domain in commit 2.
- **2026-04-22** — **Pre-execution corrections to the plan, ahead of opening the mechanical PR.** Five small drifts between plan-as-written and repo state: (1) `Models/DTOs/**` row removed from §2/§3 — no such folder exists; `Models/Requests/**` and `Models/Responses/**` fill the DTO role. (2) `InternalsVisibleTo` risk row updated in §8 — the attribute *is* currently in use (`Civiti.Api.csproj → Civiti.Tests`) and `SupabaseAdminClientTests` reaches two `internal sealed record` types on `SupabaseAdminClient`, so the attribute moves with the class in commit 4. (3) §5 NuGet table row rewritten — no separate `Microsoft.EntityFrameworkCore` runtime PackageReference exists; Npgsql.EF pulls the runtime transitively. (4) §5 adds a row for `Microsoft.EntityFrameworkCore.Tools` (stays in Civiti.Api alongside `.Design`). (5) §4 Civiti.Tests reference graph corrected — middleware tests force a retained `Civiti.Api` `ProjectReference`, not an optional one. No behaviour implications; purely factual accuracy.
