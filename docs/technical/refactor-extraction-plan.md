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
├── Civiti.Application       ← service interfaces + implementations (pure),
│                              DTOs / Requests / Responses / Notifications.
│                              Depends on: Civiti.Domain.
├── Civiti.Infrastructure    ← EF DbContext + configurations + migrations,
│                              external-world adapters (Anthropic, OpenAI,
│                              Supabase Admin, Resend, Expo, QuestPDF,
│                              JWKS cache), email templates.
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
| `Infrastructure/Localization/**` | `Localization/**` | 4 | `AchievementLocalization`, `BadgeLocalization`, `CategoryLocalization`, `PosterLocalization` — domain value tables. |
| `Infrastructure/Exceptions/AccountDeletedException.cs` | `Exceptions/AccountDeletedException.cs` | 1 | Domain-level exception. |
| `Infrastructure/Constants/DomainErrors.cs` | `Constants/DomainErrors.cs` | 1 | Domain error code table. |
| `Infrastructure/Constants/ReportTargetTypes.cs` | `Constants/ReportTargetTypes.cs` | 1 | Stable enum-like constants. |
| `Infrastructure/Constants/IssueValidationLimits.cs` | `Constants/IssueValidationLimits.cs` | 1 | Domain validation limits referenced by both services and endpoints. |

### → `Civiti.Application`

| From `Civiti.Api/` | To `Civiti.Application/` | Count | Notes |
| --- | --- | --- | --- |
| `Models/DTOs/**` | `DTOs/**` | — | All DTOs. |
| `Models/Requests/**` | `Requests/**` | — | Request records. |
| `Models/Responses/**` | `Responses/**` | — | Response records. |
| `Models/Notifications/**` | `Notifications/**` | — | In-process notification payloads. |
| `Models/Email/**` | `Email/Models/**` | — | Email data-payload types (not templates). Templates go to Infrastructure. |
| `Models/Push/**` | `Push/Models/**` | — | Push data-payload types. |
| `Services/ActivityService.cs` *(+ interface)* | `Services/**` | 2 | Pure business logic. |
| `Services/AdminService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/AuthService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/AuthorityService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/BlockService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/CommentService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/GamificationService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/IssueService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/PushTokenService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/ReportService.cs` *(+ interface)* | `Services/**` | 2 | |
| `Services/UserService.cs` *(+ interface)* | `Services/**` | 2 | |

### → `Civiti.Infrastructure`

| From `Civiti.Api/` | To `Civiti.Infrastructure/` | Count | Notes |
| --- | --- | --- | --- |
| `Data/CivitiDbContext.cs` | `Data/CivitiDbContext.cs` | 1 | |
| `Data/Configurations/**` | `Data/Configurations/**` | 18 | Fluent EF configurations. |
| `Migrations/**` | `Migrations/**` | 15 | |
| `Services/StaticDataSeeder.cs` *(+ interface)* | `Services/**` | 2 | Touches DbContext directly, classifying as infrastructure even though logic is seed-centric. |
| `Services/ClaudeEnhancementService.cs` *(+ interface)* | `Services/Claude/**` | 2 | Anthropic SDK client. |
| `Services/OpenAIModerationService.cs` *(+ interface)* | `Services/Moderation/**` | 2 | OpenAI SDK client. |
| `Services/SupabaseService.cs` *(+ interface)* | `Services/Supabase/**` | 2 | Supabase SDK client. |
| `Services/SupabaseAdminClient.cs` *(+ interface)* | `Services/Supabase/**` | 2 | Supabase Admin API. |
| `Services/EmailSenderService.cs` *(+ interface)* | `Services/Email/**` | 2 | Resend SMTP client. |
| `Services/EmailSenderBackgroundService.cs` | `Services/Email/**` | 1 | `IHostedService` implementation. |
| `Services/EmailTemplateService.cs` *(+ interface)* | `Services/Email/**` | 2 | Template rendering. |
| `Services/PushNotificationSenderBackgroundService.cs` | `Services/Push/**` | 1 | Expo push sender. |
| `Services/PosterService.cs` *(+ interface)* | `Services/Poster/**` | 2 | QuestPDF + QRCoder. |
| `Services/JwksManager.cs` *(+ interface)* | `Services/Jwks/**` | 2 | Supabase JWKS cache. |
| `Services/JwksBackgroundService.cs` | `Services/Jwks/**` | 1 | `IHostedService`. |
| `Services/AdminNotifier.cs` *(+ interface)* | `Services/AdminNotify/**` | 2 | Channels-based in-process pub/sub; lives here because it couples to email/push infrastructure. |
| `Services/AdminNotifyBackgroundService.cs` | `Services/AdminNotify/**` | 1 | `IHostedService`. |
| `Services/NotificationService.cs` *(+ interface)* | `Services/**` | 2 | In-process notifier; leave in infra to keep DI wiring uniform. Revisit if test coverage wants it in Application. |
| `Infrastructure/Email/**` | `Email/Templates/**` | 3 | `EmailLayout`, `EmailTemplates`, `EmailDataKeys`. |
| `Infrastructure/Configuration/AdminNotifyConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/ClaudeConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/ExpoPushConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/OpenAIConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/PosterConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/ResendConfiguration.cs` | `Configuration/**` | 1 | |
| `Infrastructure/Configuration/SupabaseConfiguration.cs` | `Configuration/**` | 1 | |

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
| `Infrastructure/Configuration/JwtValidationOptions.cs` | 1 | |
| `Infrastructure/Constants/ApiRoutes.cs` | 1 | HTTP routes. |
| `Infrastructure/Constants/AuthorizationPolicies.cs` | 1 | Policy-name constants; tied to the JWT policies registered in `Program.cs`. |
| `Infrastructure/Extensions/ClaimsPrincipalExtensions.cs` | 1 | JWT-claim helpers. |
| `Infrastructure/Extensions/JwtBearerExtensions.cs` | 1 | |
| `Infrastructure/Extensions/SignupMetadata.cs` | 1 | Supabase sign-up metadata parser; HTTP-request-bound. |

## 3. Namespace rewrite rules

Pure mechanical regex. Apply across every moved file.

| Pattern (old) | Replacement (new) |
| --- | --- |
| `Civiti\.Api\.Models\.Domain` | `Civiti.Domain.Entities` |
| `Civiti\.Api\.Infrastructure\.Localization` | `Civiti.Domain.Localization` |
| `Civiti\.Api\.Infrastructure\.Exceptions` | `Civiti.Domain.Exceptions` |
| `Civiti\.Api\.Infrastructure\.Constants\.(DomainErrors|ReportTargetTypes|IssueValidationLimits)` | `Civiti.Domain.Constants.$1` |
| `Civiti\.Api\.Models\.DTOs` | `Civiti.Application.DTOs` |
| `Civiti\.Api\.Models\.Requests` | `Civiti.Application.Requests` |
| `Civiti\.Api\.Models\.Responses` | `Civiti.Application.Responses` |
| `Civiti\.Api\.Models\.Notifications` | `Civiti.Application.Notifications` |
| `Civiti\.Api\.Models\.Email` | `Civiti.Application.Email.Models` |
| `Civiti\.Api\.Models\.Push` | `Civiti.Application.Push.Models` |
| `Civiti\.Api\.Services\.Interfaces` | `Civiti.Application.Services` *(for the pure-Application subset)* **or** `Civiti.Infrastructure.Services` *(for the external-dep subset)* — split by file per §2. |
| `Civiti\.Api\.Services\.(ActivityService\|AdminService\|…)` | `Civiti.Application.Services.$1` *(pure subset)* |
| `Civiti\.Api\.Services\.(ClaudeEnhancementService\|Resend…\|…)` | `Civiti.Infrastructure.Services.<Category>.$1` *(external subset)* |
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
Civiti.Tests           → Civiti.Application, Civiti.Infrastructure, Civiti.Domain
                          (drop ProjectReference to Civiti.Api)
```

Rationale for Civiti.Tests: existing tests target services, entities, and validators — all of which live in the three new libraries. The one area worth spot-checking is `Middleware/ErrorHandlingMiddlewareTests.cs` which might need to stay referencing Civiti.Api if the middleware stays there. Add a Civiti.Api project reference back only if this test fails to compile.

## 5. NuGet package split

| Package | Current (Civiti.Api) | → Civiti.Domain | → Civiti.Application | → Civiti.Infrastructure | → Civiti.Api (stays) |
| --- | :-: | :-: | :-: | :-: | :-: |
| `Anthropic.SDK` | ✓ | | | ✓ | |
| `OpenAI` | ✓ | | | ✓ | |
| `Supabase` | ✓ | | | ✓ | |
| `Microsoft.EntityFrameworkCore.*` | ✓ | | | ✓ | |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | ✓ | | | ✓ | |
| `Resend` | ✓ | | | ✓ | |
| `QRCoder` | ✓ | | | ✓ | |
| `QuestPDF` | ✓ | | | ✓ | |
| `Serilog` (+ sinks) | ✓ | | | ✓ | ✓ (for host config) |
| `Swashbuckle.AspNetCore*` | ✓ | | | | ✓ |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | ✓ | | | | ✓ |
| `Microsoft.IdentityModel.Tokens` | ✓ | | | | ✓ |
| `System.IdentityModel.Tokens.Jwt` | ✓ | | | | ✓ |

`Civiti.Domain` and `Civiti.Application` take zero NuGet packages. They compile against the .NET 10 BCL and each other only. If any validator or service turns out to need a small helper package, add it at that time with a note in the PR.

## 6. Phase order (one PR, staged commits)

Single PR, multiple commits, each one compilable and test-green. This gives us bisectability without multiple round-trips through review.

| # | Commit | What |
| --- | --- | --- |
| 1 | `chore: add empty Civiti.Domain / Civiti.Application / Civiti.Infrastructure projects to solution` | Three empty csprojs, updated `.sln`, no code movement. CI green trivially. |
| 2 | `refactor: move entities and domain constants to Civiti.Domain` | Execute the Domain move + namespace rewrite. Civiti.Api picks up ProjectReference to Civiti.Domain. Tests pass. |
| 3 | `refactor: move DTOs and pure services to Civiti.Application` | Execute the Application move. Civiti.Api picks up ProjectReference to Civiti.Application. Tests pass. |
| 4 | `refactor: move EF DbContext, migrations, and external clients to Civiti.Infrastructure` | Execute the Infrastructure move. Migrate NuGet packages. Civiti.Api picks up ProjectReference to Civiti.Infrastructure; drops the packages that moved. Tests pass. |
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
- [ ] `Civiti.Api.csproj` no longer references NuGet packages that moved (Anthropic.SDK, OpenAI, Supabase, EF Core, Resend, QRCoder, QuestPDF).

## 8. Risks and mitigations

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| EF migrations break because `CivitiDbContext` is in a different assembly | Medium | Use `dotnet ef migrations list` and a full local DB reset before opening the PR. EF Core handles cross-assembly contexts via `MigrationsAssembly()` — set it explicitly on the context's registration in Civiti.Api's `Program.cs`. |
| Tests fail on a missing `InternalsVisibleTo` | Low | No `InternalsVisibleTo` is currently used. If any test turns out to reach an `internal` method, add the attribute to the target library rather than reverting the move. |
| Generic-host DI ordering changes | Low | `Program.cs` is the only DI registration point, and it's staying in Civiti.Api. Registration logic doesn't move. |
| Serilog configuration breaks | Low | Sink setup stays in `Program.cs`; service-layer `ILogger<T>` injection works identically. |
| Namespace collisions after rewrite (e.g., two `Services` folders under different top-level namespaces both claim the same short name) | Low | Rewrite script is fully-qualified. Build will fail loudly on collision; no silent drift possible. |
| Circular project references (e.g., Civiti.Domain accidentally references Civiti.Application) | Medium | Enforce via csproj review — Civiti.Domain has zero `<ProjectReference>`. A CI step adding `<PackageReference>` and `<ProjectReference>` validation is out of scope; the reviewer is the gate. |
| `Infrastructure/Constants/IssueValidationLimits` used both from endpoints (Civiti.Api) and services (Civiti.Application) creates a Domain-is-referenced-by-both case | None (works) | That's exactly what putting it in Civiti.Domain is for — both referencing projects already depend on Domain. |

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
- **2026-04-22** — `NotificationService` (in-process) is classified as Infrastructure despite having no external deps, because it lives alongside `AdminNotifier` in the notification-coordination stack. Revisit during implementation if test coverage benefits from moving it to Application.
