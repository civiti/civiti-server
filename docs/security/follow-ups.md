# Security follow-ups

> **Living document.** Captures security work that has been identified but not yet
> tackled, plus things noticed in passing during other work that would warrant a
> dedicated audit. Update as items are addressed (move to "Closed" with the PR
> reference) or as new ones are surfaced.
>
> **Scope.** This is the project's internal backlog. It is not a vulnerability
> disclosure policy — for reporting issues from outside the team, use whatever
> channel `civiti.app` advertises (or open a private GitHub security advisory).
>
> **Companion document.** [`mcp-prompt-injection-review-2026-05-05.md`](./mcp-prompt-injection-review-2026-05-05.md)
> is the prompt-injection-specific audit whose actionable findings have been
> closed. This file is broader: items it didn't cover plus items it deferred.

## Conventions

Each item carries:

- **Severity** — best-effort categorisation. We don't know exploitability for items
  we haven't audited; severity reflects "what's at stake if it turns out to be
  wrong" rather than confirmed risk.
- **Status** — `open`, `in-progress`, `closed (PR #N)`.
- **Why it matters** — the threat model in one or two sentences.
- **Where to look** — exact files / commands / dashboard locations.
- **Notes** — context, prior work, anything that biases an investigator's first
  hour.

---

## Open

### Auth / DCR end-to-end audit

- **Severity:** medium-high (untested, but the surface is the load-bearing trust boundary).
- **Status:** open.
- **Why it matters.** `Civiti.Auth` has been touched iteratively across many PRs in
  the v1c series (#130–#138). Each PR was sound in isolation, but no fresh
  end-to-end review has been done on the OAuth / DCR / consent flow as a coherent
  whole. The code itself flags `AdminScopeFilter` as *"the SOLE per-client scope
  gate"* — that load-bearing piece deserves dedicated scrutiny rather than
  piece-meal scope-related fixes.
- **Where to look.**
  - `Civiti.Auth/Endpoints/AuthorizeEndpoint.cs`, `RegisterEndpoint.cs`, `AdminScopeFilter.cs`
  - `Civiti.Auth/Pages/Consent.cshtml.cs` (consent-cookie lifecycle)
  - `Civiti.Auth/Authentication/ConsentContext.cs` (encrypted consent payload)
  - `Civiti.Auth/Program.cs` (OpenIddict server config — `IgnoreScopePermissions`,
    `IgnoreResourcePermissions`, `DisableScopeValidation` are all on; the inline
    comments explain why, and the comments themselves call out the foot-guns
    each toggle introduces)
- **Threat-model dimensions to cover.**
  - **Authz escalation.** Can a DCR-registered client mint a token with
    `civiti.admin.*`? `AdminScopeFilter` is the static gate; verify every call
    site (`AuthorizeEndpoint` SignIn, `Consent` render + POST, refresh-token
    rotation in TokenEndpoint).
  - **Scope leakage in discovery.** AS `/.well-known/openid-configuration` and
    RS `/.well-known/oauth-protected-resource` should both expose only the
    citizen scopes. Verified once via PRs #135 and #138; re-verify against the
    current live deploy.
  - **Token contract integrity.** `refresh_token` grant is advertised in DCR
    response — verify it's reliably obtainable end-to-end (PR #137 closed the
    `offline_access` gap; check there's been no regression).
  - **Consent-cookie lifecycle.** Encrypted, one-time-use, `SameSite=Strict`,
    `Path=/Consent` (PR #139 hardened this). Confirm the invariants still hold:
    every successful path consumes the cookie; the evaporated-session path
    cleans up; no path leaves it alive past 10 minutes.
  - **Cross-tenant leakage.** Civiti is single-tenant today, but if a future
    multi-tenant scheme lands (e.g. per-county admins), the scope model needs
    to be re-read with that in mind.
- **Suggested investigation method.** Same shape as the prompt-injection review:
  spawn a focused subagent with a written threat model targeted at
  `Civiti.Auth/`, output a Markdown report, treat the findings as a backlog
  similar to this one.

### Civiti-Server prod `SUPABASE_SERVICE_ROLE_KEY` is set to a Resend-prefix value

- **Severity:** **high** if the key is wrong (the service-role key bypasses
  Supabase RLS and grants full DB access via the Supabase API; a misconfigured
  value means either the Supabase admin path is broken in prod, or the slot is
  holding the wrong secret entirely). Severity drops to low/cosmetic if it's
  intentional dead config.
- **Status:** in-progress (known since 2026-05-04, not yet fixed).
- **Why it matters.** During the 2026-05-04 production deploy fix on Civiti-MCP,
  while reading the `Civiti-Server` prod variables we noticed
  `SUPABASE_SERVICE_ROLE_KEY` carries a value starting with `re_…` — the prefix
  used by Resend keys, not Supabase. Three possibilities, ranked by likelihood
  given other observations:
  1. **Paste mix-up** — someone moved a Resend key into the wrong env var slot
     and the real Supabase service-role key is missing entirely. Most likely
     given the `RESEND_FROM_EMAIL ` (trailing-space duplicate) anomaly on the
     same service suggests other paste-time errors.
  2. **Dead field** — the service uses `DATABASE_URL` for direct Postgres
     access (which it does, per `appsettings.Production.json` and the connection
     string format) and never actually consumes `SUPABASE_SERVICE_ROLE_KEY`. In
     that case the value is dead config — but the slot name implies it's read,
     and a future code change might start reading it.
  3. **Intentional** — unlikely given the value shape doesn't match either
     legacy Supabase JWT format (`eyJ…`) or the new `sb_secret_…` format.
- **Where to look.**
  - Railway dashboard → `Civiti-Server` → `production` env → Variables.
  - Confirm by comparing against the development env, and against what
    Supabase's project dashboard shows as the current service-role key for
    project `cmkznjhbwmcgtbnynkft`.
  - Search the codebase for actual reads:
    ```bash
    grep -rn "SUPABASE_SERVICE_ROLE_KEY\|SUPABASE_SERVICE_KEY\|Supabase:ServiceRoleKey\|ServiceRoleKey" --include='*.cs' --include='*.json'
    ```
- **What to do.**
  - **If the key is consumed in code:** rotate it. Generate a new service-role
    key in Supabase (which invalidates the current one), set it on the
    affected env var slot, redeploy. Whatever the `re_…` value is gets
    invalidated as a Resend key only if it actually was a Resend key in
    rotation; either way the rotated Supabase key becomes the source of truth.
  - **If the key is not consumed in code:** delete the env var entirely so the
    slot doesn't carry stale or wrong-typed config that could mislead a future
    reader.
  - Either way: while you're in there, fix the `RESEND_FROM_EMAIL ` (trailing
    space) duplicate on the same service.

### Secret-management audit (everything else)

- **Severity:** medium (unknown count of other anomalies; one already known
  and tracked separately above).
- **Status:** open.
- **Why it matters.** Production env vars are stored in plaintext on Railway.
  Beyond the known `SUPABASE_SERVICE_ROLE_KEY` anomaly, no systematic sweep
  has been done across the three services × two environments to verify each
  value matches its key name and that nothing else has the same kind of
  paste-mix-up.
- **Where to look.**
  - Railway dashboard → each service (`Civiti-Server`, `Civiti-Auth`,
    `Civiti-MCP`) → Variables tab, both `development` and `production`
    environments.
  - Or via the CLI:
    ```bash
    railway variables --service Civiti-Server --environment production --kv
    railway variables --service Civiti-Auth   --environment production --kv
    railway variables --service Civiti-MCP    --environment production --kv
    ```
- **Suggested checks.**
  - Each `*_API_KEY`, `*_SERVICE_KEY`, `*_SECRET` value matches the prefix
    convention of its named service:
    - **Supabase JWTs** — start with `eyJ` (header.payload.signature, base64url).
      This is the format service-role and legacy anon keys use.
    - **Supabase API keys (newer format)** — `sb_publishable_…` (safe to
      expose, equivalent to the anon JWT) and `sb_secret_…` (server-only,
      equivalent to the service-role JWT).
    - **Resend** — `re_…`.
    - **OpenAI** — `sk-…` (project keys are `sk-proj-…`).
    - **Anthropic** — `sk-ant-…`.
    - **Railway internal Postgres URLs** — `postgresql://postgres:…@postgres.railway.internal:5432/railway`.
  - No copy-paste duplication where a value clearly belongs to a different
    service. (The known case is tracked separately above; this sweep is for
    finding others.)
  - No keys accidentally committed to git. The sweep regex needs to cover
    both Supabase formats and every OpenAI / Anthropic / Resend key shape — a
    pattern that requires `sk-proj-` or `sk-ant-` would miss the legacy
    `sk-…` OpenAI keys still in circulation, and a Supabase pattern that
    only checks `sb_secret_` would miss legacy JWT-style service-role keys
    (which start with `eyJ` and are visually indistinguishable from any
    other base64url payload):
    ```bash
    git log --all -p | grep -E '(sb_secret_|sb_publishable_)[A-Za-z0-9_-]{10,}|sk-(proj-|ant-)?[A-Za-z0-9_-]{20,}|re_[A-Za-z0-9]{20,}|eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{20,}'
    ```
    Notes on this regex:
    - `sk-(proj-|ant-)?…` — the `?` makes the prefix optional, so legacy
      OpenAI `sk-…` keys, project keys (`sk-proj-…`), and Anthropic keys
      (`sk-ant-…`) all match.
    - The trailing JWT pattern is intentionally conservative — three
      base64url-padded segments separated by dots — so it catches Supabase
      JWTs without also matching every `eyJ` substring (e.g. unrelated base64
      blobs in test fixtures).
    - Run on a clean clone; review every match individually rather than
      trusting the sweep blindly. False positives (e.g. test-fixture
      placeholders, comment lines) are easy to identify on inspection.

### `mark_email_sent` rate-limit per-IP under shared NAT

- **Severity:** low (not a security finding per se — abuse-prevention degradation).
- **Status:** open — observe before acting.
- **Why it matters.** `Civiti.Mcp/Tools/PublicIssueTools.cs` `mark_email_sent`
  rate-limits at 1 request per IP per issue per hour. Most MCP traffic from
  Claude Desktop / claude.ai goes through Anthropic's relay, which means many
  legitimate users share an upstream IP. One user's `mark_email_sent` could
  silently block another user's for an hour on the same issue.
- **Where to look.**
  - `Civiti.Infrastructure/Services/IssueService.cs` `IncrementEmailCountAsync`
    around the cache-key construction (`"email-cooldown:{issueId}:{clientIp}"`).
  - `Civiti.Mcp/Program.cs` proxy-trust + X-Forwarded-For peel.
- **What to do.** Wait for production traffic to actually flow through MCP
  before changing this — the noted memory `reference_railway_proxy_chain.md`
  documents that Railway appends 2 hops to XFF and the real client lands at
  `XFF[len-2]`. If shared-NAT collisions show up in logs as "false positive"
  rate-limit hits, switch the cooldown key from raw IP to authenticated user-id
  on the `/mcp` mount (use `IMcpCitizenContext.TryResolveCitizenAsync`,
  introduced in PR #143) and keep raw-IP partitioning only for `/mcp/public`.

### Dependency CVE scan

- **Severity:** unknown until run.
- **Status:** open.
- **Why it matters.** No CVE sweep has been done against the project's NuGet
  packages in this session. The OAuth/MCP stack pulls in OpenIddict,
  ModelContextProtocol, Serilog, etc. — known surfaces for security bulletins.
- **What to do.**
  ```bash
  dotnet list package --vulnerable --include-transitive
  ```
  Run on each of the three Web SDK projects (`Civiti.Api`, `Civiti.Auth`,
  `Civiti.Mcp`). For any reported vulnerability:
  - Look up the GHSA / CVE.
  - Check whether the affected code path is reachable in this codebase.
  - Patch via package update, or document the rationale for accepting the
    finding (e.g. "vuln only triggers if `X` is enabled, which we never call").

### Admin two-step pattern (forward-looking)

- **Severity:** N/A today (no admin tools exist); medium when they land.
- **Status:** open — design only, awaits implementation.
- **Why it matters.** `Civiti.Mcp/docs/tool-inventory.md §3.2` and
  `auth-design.md §9` describe a `propose_*` → `confirm_admin_action` pattern as
  defense against single-call prompt injection on destructive admin actions.
  Today, no admin tool class is registered in `Civiti.Mcp/Program.cs`. When
  admin tools are added:
  - The pending-action TTL must be enforced server-side.
  - The `confirm_admin_action` tool's response must NOT echo the proposed
    action's payload back verbatim (otherwise injection content from the
    item-under-review is re-surfaced after confirm).
  - The `propose_*` step's response must wrap user-supplied fields with the
    `[Untrusted]` envelope from PR #144 — that's where the moderator's LLM
    most needs the data/code separation, and it's also where the temptation
    to drop the wrap (since the moderator "needs to read the content")
    will be highest.

### Logging hygiene review

- **Severity:** low-medium (privacy posture).
- **Status:** open.
- **Why it matters.** Token round-trips through `Civiti.Auth` and tool calls
  through `Civiti.Mcp` log diagnostic data. We haven't done a sweep for what
  user-identifying data, scope claims, or near-secrets land in production logs.
- **Where to look.**
  - `Civiti.Mcp/Authorization/McpCitizenContext.cs` already logs sub claims on
    scope rejections (`McpCitizenContext.cs:80-81`) — by design, but worth
    confirming against any privacy posture.
  - `Civiti.Auth/Pages/Login.cshtml.cs`, `Consent.cshtml.cs` — login + consent
    flow log lines.
  - `Civiti.Infrastructure/Services/Moderation/OpenAIModerationService.cs` —
    confirm moderation responses don't log the rejected content verbatim.
  - Centralized: search the codebase for `LogInformation`, `LogWarning`,
    `LogError` calls in any auth / consent / token path.

### Database-layer security posture

- **Severity:** unknown.
- **Status:** open.
- **Why it matters.** Civiti uses PostgreSQL via EF Core. Two areas not yet
  reviewed:
  - **Row-level security (RLS).** Supabase encourages RLS on every table that
    holds user-scoped data. EF Core integration with Supabase RLS is non-trivial
    — confirm whether RLS is on at the DB level and what role the EF
    `DATABASE_URL` connection runs as. If the connection is a privileged
    `service_role` and the app does its own row-scoping in code, that's the
    intended pattern but worth documenting.
  - **Migration safety.** Per memory `reference_railway_branch_topology.md`,
    migrations only reach prod via `master → production` merge. Confirm
    `Civiti.Infrastructure/Migrations/` does not contain any migrations that
    would break under concurrent writes (NOT NULL adds without backfill,
    schema changes without `IF NOT EXISTS` guards, etc.).

---

## Closed

### LOW — fail-open moderation policy

- **Closed in:** [PR #145](https://github.com/civiti/civiti-server/pull/145).
- **Outcome.** Both `Civiti.Api` and `Civiti.Mcp` now throw at startup outside
  Development if `OPENAI_API_KEY` is missing. Runtime fail-open on transient
  OpenAI timeouts/exceptions is intentionally preserved (outages shouldn't
  block legitimate users), but missing-config-in-prod is a deployment mistake
  surfaced before traffic lands.

### Out-of-scope #1 — County / City / District length caps

- **Closed in:** [PR #145](https://github.com/civiti/civiti-server/pull/145).
- **Outcome.** `UserService.UpdateUserProfileAsync` enforces 100-char caps in
  code, mirroring the DisplayName / PhotoUrl pattern from PR #141.

### Out-of-scope #2 — authority emails

- **Closed in:** [PR #144](https://github.com/civiti/civiti-server/pull/144).
- **Outcome.** `IssueAuthorityResponse.Name` / `Email` are tagged `[Untrusted]`
  so custom-authority free-text routes through the quarantine envelope on the
  MCP path. MCP `create_issue` does NOT expose the `Authorities` field
  (REST-only); admin approval gates publication of custom authorities. Coverage
  is sufficient for prompt-injection; information-disclosure aspect (a custom
  authority email being a personal address) remains gated by admin review.

### Original review HIGH and MEDIUM findings

- **HIGH — `create_issue` moderation gap.** Closed in [PR #141](https://github.com/civiti/civiti-server/pull/141).
- **HIGH — `update_my_profile` moderation gap.** Closed in [PR #141](https://github.com/civiti/civiti-server/pull/141).
- **MEDIUM — `search_issues` / `get_issue` block-list bypass.** Closed in [PR #143](https://github.com/civiti/civiti-server/pull/143).
- **MEDIUM — untrusted-content envelope.** Closed in [PR #144](https://github.com/civiti/civiti-server/pull/144).

---

## How to update this document

When opening a follow-up PR that addresses an item here:

1. Move the item from **Open** to **Closed**.
2. Add the PR link and a one-line outcome summary.
3. Don't delete the item entry — the audit trail of "we noticed this, here's
   what we did, here's when" is the value of this file.

When you notice a new concern during unrelated work:

1. Add a new entry under **Open** with severity / why it matters / where to
   look. Don't sit on it — even a one-line note is better than losing it to
   chat history.
2. If you can't categorise severity, write `unknown` and explain what you
   need to investigate to find out. That itself is a useful signal.
