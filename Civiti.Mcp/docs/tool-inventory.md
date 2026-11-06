# Civiti.Mcp — Tool, Resource, and Prompt Inventory

> **Status:** Living document. Section numbers are stable; new sections
> append to §8+.
> Last revision: 2026-11-06 — rebuilt §1 and §2 from `Civiti.Mcp/Tools/*.cs`;
> marked §3–§6 as designed but not built.

**21 tools are live**, all listed in §1 and §2. Nothing else here is running: there are no admin tools, MCP resources or MCP prompts. §3–§6 keep that design for later phases.

`scripts/check-mcp-tool-inventory.py` runs in CI and fails when the §1 and §2 tables stop matching the code. For URLs, auth and rate limits, see the [technical reference](../README.md).

## Guiding principles

1. **Curated, not exhaustive.** A small set of well-named tools with strong
   descriptions outperforms a 1:1 mirror of the REST API. Agents struggle
   when two tools overlap.
2. **Tools wrap services, not endpoints.** Each handler calls an existing
   method on `Civiti.Application` so business rules and moderation are
   identical to the REST path. No logic duplication.
3. **No tool returns raw DB entities.** Responses are DTOs, and user-supplied
   strings are wrapped as untrusted content (see §2.3).
4. **Public tools mirror the public REST surface exactly.** If the REST
   endpoint is anonymous, its MCP counterpart is anonymous on the same
   terms (filters and public-fields-only filtering).

## Endpoints

Both mounts serve the same MCP server, so `tools/list` returns all 21 tools on each. What differs is what a call can do:

- **`/mcp/public`** needs no token. Public tools work. Authenticated tools are listed, but calling one returns `{ok: false, reason: "unauthenticated"}`.
- **`/mcp`** needs a bearer token on every request, including `tools/list`. Without one it returns `401`. See [`auth-design.md`](auth-design.md).

The table columns in §1 and §2 mean:

| Column | Values |
| --- | --- |
| Endpoint | **Public**: works anonymously, on either mount. **Authenticated**: needs a token, so in practice `/mcp`. |
| Scope | The scope the handler checks. `none` means it checks nothing. |
| Access | **Read** or **Write**, as seen by the caller. |

## Rate-limit classes

**None of these classes are built.** Every call counts against one shared limit per source IP; see [Rate limits](../README.md#rate-limits) for what runs today. Per-user limits are tracked in #118. The table below is the design.

| Class | Per-session | Per-user (global) | Notes |
| --- | --- | --- | --- |
| `read.public` | — | — | **30 / min per source IP.** Anonymous tools on `/mcp/public`. No user or session context. |
| `read.cheap` | 60 / min | 300 / min | Profile/leaderboard reads, cached. |
| `read.search` | 20 / min | 120 / min | Authenticated search, hits DB harder. |
| `write.citizen` | 6 / min | 20 / hour | Issue creation, voting, comments. |
| `write.admin` | 10 / min | 60 / hour | Approve/reject. Two-step confirm required. |
| `ai.claude` | Shared budget with REST `/enhance-text` (10 / min / user). | — | Runs through `ClaudeEnhancementService`. |

## 1. Public (anonymous) tools

**Intent:** let an AI agent browse civic data and help a non-user draft a petition email without requiring signup. The citizen still composes and sends the petition from their own inbox; Civiti only counts it via `mark_email_sent`. This mirrors how the Civiti web UI works for unauthenticated visitors.

| Tool | Endpoint | Scope | Access | Purpose |
| --- | --- | --- | --- | --- |
| `search_issues` | Public | none | Read | Search publicly visible issues, with paging, filters and sorting. |
| `get_issue` | Public | none | Read | Get the full details of one issue. |
| `list_authorities` | Public | none | Read | List active local authorities, by city or district. |
| `get_categories` | Public | none | Read | List the issue categories with their Romanian labels. |
| `get_leaderboard` | Public | none | Read | Rank contributors by points, issues, resolved issues or votes. |
| `mark_email_sent` | Public | none | Write | Add one to an issue's public petition-email counter. |
| `whoami` | Public | none | Read | Show the caller's subject, scopes and role, or `authenticated: false`. Diagnostic; removal is tracked in #116. |

### Parameters worth knowing

- `search_issues`: `page`, `pageSize` (1–100, default 12), `category`, `urgency`, `status` (comma-separated, default `Active`), `district`, `address`, `sortBy` (`date`, `emails`, `votes` or `urgency`; default `date`), `sortDescending` (default `true`). An unrecognised `category`, `urgency` or `status` is ignored rather than rejected.
- `get_leaderboard`: `period` (`all`, `week`, `month` or `year`; default `all`), `category` (`points`, `issues`, `resolved` or `votes`; default `points`), `limit` (1–50, default 50).
- `list_authorities`: `city`, `district`.

### Behaviour and safety

- `search_issues` only returns `Active` and `Resolved` issues, whatever `status` asks for. `get_issue` does the same for anonymous calls, but with a token it also returns the caller's own issues in any status. These rules live in `IssueService`, so REST and MCP can't drift apart.
- With a token, `search_issues` and `get_issue` also hide content from users the caller has blocked and add `HasVoted`. Anonymous calls get neither.
- `mark_email_sent` is the only anonymous write, and it behaves the same on both mounts: it bumps the counter and nothing else, with no activity row or points, even with a token. It only accepts `Active` issues and allows one call per IP, per issue, per hour. The client IP comes from the proxy-trust middleware in `Program.cs`.

## 2. Citizen tools

### 2.1 Read

| Tool | Endpoint | Scope | Access | Purpose |
| --- | --- | --- | --- | --- |
| `get_my_profile` | Authenticated | `civiti.read` | Read | Get the user's Civiti profile. |
| `get_my_gamification` | Authenticated | `civiti.read` | Read | Get the user's points, level, badges, achievements and streak. |
| `list_my_issues` | Authenticated | `civiti.read` | Read | List the issues the user created, filtered by status and sorted by date, status or emails. |
| `list_my_activity` | Authenticated | `civiti.read` | Read | List activity on issues the user follows. |
| `list_my_blocked_users` | Authenticated | `civiti.read` | Read | List the users this user has blocked. |
| `generate_petition_body` | Authenticated | `civiti.read` | Read | Compose a Romanian petition email body for an existing issue the caller can see. |

**`generate_petition_body` belongs to the issue flow, not to petition writing.** It backs the starter email on an issue's page in the web app. Petitions are written by the `civiti-petitions` skill, which will be hosted at `https://civiti.ro/skills/civiti-petitions/SKILL.md` once civiti-web#84 lands.

The tool composes the body with Claude and falls back to a fixed template if Claude is unavailable. Results are cached per issue for 24 hours by default, and `regenerate=true` asks for a fresh wording. Fresh compositions are limited to 10 per minute per user; cached results don't count.

### 2.2 Write

| Tool | Endpoint | Scope | Access | Purpose |
| --- | --- | --- | --- | --- |
| `create_issue` | Authenticated | `civiti.write` | Write | Create an issue, which waits for admin approval before it is public. |
| `update_my_profile` | Authenticated | `civiti.write` | Write | Update the user's display name, photo URL, county, city or district. |
| `vote_on_issue` | Authenticated | `civiti.write` | Write | Add or remove the user's vote on an issue. |
| `add_comment` | Authenticated | `civiti.write` | Write | Comment on an issue, or reply to a comment. |
| `vote_on_comment` | Authenticated | `civiti.write` | Write | Mark a comment as helpful, or remove that mark. |
| `report_content` | Authenticated | `civiti.write` | Write | Report an issue or a comment to the moderators. |
| `block_user` | Authenticated | `civiti.write` | Write | Hide another user's content from this user. |
| `unblock_user` | Authenticated | `civiti.write` | Write | Remove a user from the block list. |

Both vote tools take `direction` as `up` or `remove`. `add_comment` allows one comment per 10 seconds per user per issue, and rejects identical text repeated within 5 minutes.

### 2.3 Errors, moderation and untrusted content

Tools report expected failures as a normal result with `{ok: false, reason, message}`, so an agent can explain the problem instead of retrying:

| `reason` | Meaning |
| --- | --- |
| `unauthenticated` | An authenticated tool was called without a token. |
| `missing_scope` | The token doesn't carry the scope the tool needs. |
| `missing_subject` | The token has no `sub` claim. |
| `user_profile_missing` | The account has no Civiti profile yet. |
| `moderation_rejected` | Moderation blocked the text. |
| `invalid_input` | A value is outside the allowed list, such as `direction`. |
| `not_found` | `generate_petition_body` can't see the issue. |
| `rate_limited` | `generate_petition_body` hit the per-user limit. |
| `service_error` | The service refused the operation; `message` says why. |
| `ip_unresolvable` | `mark_email_sent` couldn't determine the client IP. |

`mark_email_sent` puts the service's own error in `reason`, for example `RATE_LIMITED`, and has no `message`. Other failures, such as `add_comment` hitting its limit, come back as a generic MCP tool error (`isError: true`) without the service's message.

`create_issue`, `update_my_profile` (display name only) and `add_comment` send text through the same OpenAI moderation as the REST API. Moderation lets content through if OpenAI times out or fails, so it reduces abuse rather than guaranteeing a block.

User-supplied strings in tool results, marked `[Untrusted]` on the response DTOs, are returned as an envelope with `value`, `untrusted` and `source` fields rather than as bare strings. See `UntrustedStringConverter` and the [prompt-injection review](../../docs/security/mcp-prompt-injection-review-2026-05-05.md).

## 3. Admin tools

> **Not built.** No admin tool exists in `Civiti.Mcp/Tools`, no tool checks an admin scope, and neither `UserProfile.McpAdminAccessEnabled` nor `McpPendingAdminActions` exists. This section keeps the design for a later phase.

All admin tools require `civiti.admin.read` or `civiti.admin.write` **and** the underlying Supabase role `admin` **and** `UserProfile.McpAdminAccessEnabled` to be `true`. See [`auth-design.md#9`](auth-design.md#9-admin-specific-hardening).

### 3.1 Read

| Tool | Scope | Backing service | Input summary | Rate class |
| --- | --- | --- | --- | --- |
| `list_pending_issues` | `civiti.admin.read` | `AdminService.GetPendingIssuesAsync(take, skip)` | paging | `read.cheap` |
| `get_pending_issue` | `civiti.admin.read` | `AdminService.GetIssueAsync(id)` | `id` | `read.cheap` |
| `get_moderation_stats` | `civiti.admin.read` | `AdminService.GetModerationStatsAsync()` | none | `read.cheap` |
| `get_platform_statistics` | `civiti.admin.read` | `AdminService.GetStatisticsAsync()` | none | `read.cheap` |
| `list_reports` | `civiti.admin.read` | `ReportService.ListAsync(status?)` | `status?` | `read.cheap` |

### 3.2 Write (two-step confirmation)

These tools return a pending-action id rather than mutating immediately. A second call to `confirm_admin_action` within 5 minutes is required to commit. See [`auth-design.md#9`](auth-design.md#9-admin-specific-hardening).

| Tool | Scope | Backing service | Input summary | Rate class | Audit |
| --- | --- | --- | --- | --- | --- |
| `propose_approve_issue` | `civiti.admin.write` | → pending action | `issueId`, `notes?` | `write.admin` | — (prep only) |
| `propose_reject_issue` | `civiti.admin.write` | → pending action | `issueId`, `reason`, `notes?` | `write.admin` | — |
| `propose_request_changes` | `civiti.admin.write` | → pending action | `issueId`, `requestedChanges` | `write.admin` | — |
| `propose_bulk_approve` | `civiti.admin.write` | → pending action | `issueIds: uuid[]` (max 20) | `write.admin` | — |
| `confirm_admin_action` | `civiti.admin.write` | resolves pending action → `AdminService.*` | `pendingActionId` | `write.admin` | `AdminAction{Source=mcp, Tool=...}` |
| `cancel_admin_action` | `civiti.admin.write` | drops pending action | `pendingActionId` | `write.admin` | — |

### Pending-action storage

Pending actions must survive across requests — `propose_*` and `confirm_admin_action` may hit different pods in a multi-process Railway deployment — and must expire cleanly. They live in PostgreSQL, not in-memory:

```
McpPendingAdminActions
────────────────────────────────────────────────────────────────
Id             uuid          PK
AdminUserId    uuid          FK → UserProfile (the admin who proposed)
McpSessionId   uuid          FK → McpSessions (the session that proposed)
ActionType     text          approve | reject | request_changes | bulk_approve
Payload        jsonb         tool-specific input (issueId, reason, issueIds[], …)
CreatedAt      timestamptz
ExpiresAt      timestamptz   CreatedAt + 5 min
ConfirmedAt    timestamptz   (nullable)
CanceledAt     timestamptz   (nullable)
```

Rules:

- `confirm_admin_action(id)` must be invoked by the **same admin user** that created the pending action (`AdminUserId` match). Session match is not required — an admin who proposed from one Claude client and confirms from another is legitimate.
- Expired or already-resolved rows return a structured error (`{ok: false, reason: "pending_action_expired"}` / `"pending_action_already_resolved"`) so the agent can explain the state.
- A lightweight janitor job (part of the MCP-session revalidation sweep described in [`auth-design.md` §4](auth-design.md#4-trust-boundary-civitiauth-is-the-identity-edge)) deletes rows where `ExpiresAt < now() - 24h` for housekeeping.
- **Not stored in-memory.** In-process storage would lose proposals on pod restart, break horizontal scale-out (confirm lands on a different pod than propose), and quietly drop actions during Railway deploys.

## 4. AI-assisted tools

> **Not built.** The only AI-backed tool today is `generate_petition_body` (§2.1).

### `draft_issue_description`

| Field | Value |
| --- | --- |
| Scope | `civiti.read` (writes no data; LLM-budget cost is bounded by the rate limit below) |
| Backing service | `ClaudeEnhancementService.EnhanceAsync(rawText, category)` |
| Input | `rawText`, `category`, `locale?` |
| Output | `{draft: string, suggestedCategory?: string, suggestedUrgency?: string}` |
| Rate class | `ai.claude` — shared budget with REST `/enhance-text` |

Usage note in the tool description: instructs the agent to present the draft to the user for confirmation before calling `create_issue`.

## 5. Resources

> **Not built.** The server registers no MCP resources.

Resources are URI-addressable reads the agent can fetch without spending a tool call.

| URI template | Returns | Endpoint | Requires scope |
| --- | --- | --- | --- |
| `civiti://me` | Current user's profile + gamification summary | `/mcp` | `civiti.read` |
| `civiti://issue/{id}` | Full issue detail | `/mcp` + `/mcp/public` | `civiti.read` on `/mcp`; none on `/mcp/public` (public fields only) |
| `civiti://authority/{city}` | Authorities servicing that city/district | `/mcp` + `/mcp/public` | `civiti.read` on `/mcp`; none on `/mcp/public` |
| `civiti://leaderboard/global` | Top N users globally | `/mcp` + `/mcp/public` | `civiti.read` on `/mcp`; none on `/mcp/public` |
| `civiti://leaderboard/city/{city}` | Top N in that city | `/mcp` + `/mcp/public` | `civiti.read` on `/mcp`; none on `/mcp/public` |
| `civiti://categories` | Enum of categories with localized labels | `/mcp` + `/mcp/public` | none (public) |
| `civiti://admin/pending-queue` | Summary of pending issues | `/mcp` | `civiti.admin.read` |
| `civiti://admin/report-queue` | Open reports summary | `/mcp` | `civiti.admin.read` |

## 6. Prompts (optional, later)

> **Not built.** The server registers no MCP prompts.

MCP prompts are reusable templates the client can offer as commands in its UI. Proposed starter set:

| Prompt | Trigger |
| --- | --- |
| `/civiti-find-and-petition` *(public)* | Walks an anonymous user through `search_issues` → `get_issue` → drafts a petition email → reminds the user to send from their inbox → calls `mark_email_sent`. |
| `/civiti-report-issue` | Walks the authenticated user through `draft_issue_description` + `create_issue`. |
| `/civiti-my-dashboard` | Fetches `civiti://me`, `list_my_issues`, `list_my_activity` and summarizes. |
| `/civiti-review-queue` (admin) | Fetches `civiti://admin/pending-queue` and offers the agent a moderation workflow. |

Prompts are low priority — iterate on them once we see how users actually interact.

## 7. Phased rollout

1. **v0 (public smoke test):** `/mcp/public` endpoint with §1 tools. No OAuth, no session. **Shipped.**
2. **v1 (authenticated read):** `/mcp` endpoint with §2.1 citizen read tools and the OAuth flow against Civiti.Auth. **Shipped.**
3. **v2 (citizen write):** §2.2 citizen write tools. **Shipped**, without `draft_issue_description` or the §5 resources.
4. **v3 (admin preview, gated):** §3 admin tools with the two-step confirm flow. Not started.
5. **v4:** Prompts (§6), broader client onboarding beyond the initial allow-list. Not started.

## 8. Open questions

1. **Naming conventions:** `create_issue` vs. `report_issue` vs. `submit_issue`. REST uses "create". Agents read these names directly — which is clearest to an LLM?
2. **Pagination style:** cursor-based (opaque token, ordering stable) vs. take/skip. REST uses take/skip (and we've mirrored it for `search_issues`); agents handle cursor tokens fine, but the divergence from REST adds cognitive load. Recommend: keep take/skip to stay aligned with REST; revisit if we hit ordering-instability bugs in practice.
3. **Bulk tools beyond admin:** is there value in `vote_on_issues (ids[])` for citizens, or does that invite spam? Recommend: no — keep citizen writes one-at-a-time.
4. **City-scoped `get_leaderboard`.** An earlier draft of §1 called for `get_leaderboard(scope: global|city, city?, take)`. The underlying `IGamificationService.GetLeaderboardAsync` exposes `(period, category, limit)` only — there is no city dimension in the service or on the REST endpoint. §1 now mirrors the existing signature. If the city-scoped leaderboard turns out to matter for MCP agents (e.g. "who's leading in Cluj this month?"), add it on the REST side first and then expose the extra filter through the MCP tool — same guiding principle as "public tools mirror the public REST surface exactly".

## 9. Resolved decisions (log)

- **2026-04-21** — **Public (anonymous) MCP surface in scope for v1.** Mirrors the existing public REST endpoints (`GET /api/issues`, `GET /api/issues/{id}`, `GET /api/issues/{id}/poster`, `POST /api/issues/{id}/email-sent`). Exposed on a dedicated `/mcp/public` endpoint with IP-based rate limiting. Petition-sending is still citizen-originated from their own inbox; Civiti only counts via `mark_email_sent`.
- **2026-04-21** — **`search_issues` filter signature aligned with REST.** Filters: `page`, `pageSize` (1–100), `category`, `urgency`, `status` (comma-separated), `district`, `address`, `sortBy`, `sortDescending`. No invented `city`, `county`, or `query` params; if we want those, they go on the REST endpoint first.
- **2026-04-21** — **Rollout order: public first, authenticated second.** Public endpoint is the simpler smoke test (no OAuth dependencies) and validates the transport + service-wrapping shape before we layer auth on top.
- **2026-04-23** — **§1 backing-service references corrected to match the actual `Civiti.Application` surface** (caught during the v0 skeleton audit, ahead of opening the Civiti.Mcp scaffolding PR):
  - `list_authorities` → `IAuthorityService.GetActiveAuthoritiesAsync(city?, district?, search?)` (the earlier `ListForLocationAsync(city, district?)` reference was aspirational; the real method is a superset and we now pass `search: null` from the MCP handler).
  - `get_leaderboard` → `IGamificationService.GetLeaderboardAsync(period, category, limit)`. The earlier `(scope, city?, take)` signature imagined a city dimension that neither the service nor the REST endpoint exposes. City-scoped leaderboards deferred to §8 open question #4; if needed, ship on REST first.
  - `get_categories` → `CategoryLocalization.GetAll()` (static helper in `Civiti.Application.Localization`). No `IStaticDataService` exists; the data is already in a static class. If a second static-data tool lands later, wrap in a service at that point.
- **2026-11-06** — **Inventory rebuilt from `Civiti.Mcp/Tools/*.cs`, and a CI check added.** Earlier revisions described plans as if they were live. Corrections: every tool is listed on both mounts, and the 14 authenticated ones fail closed on `/mcp/public`; `mark_email_sent` does the same thing on both mounts, so the "enriched when authenticated" variant was never built; `search_issues` sorts by `emails`, not `popularity`; `get_leaderboard` also takes `year`, and its categories are `points`, `issues`, `resolved` and `votes`; the per-class rate limits and the `Source = "mcp"` audit stamp don't exist. The admin, AI, resource and prompt designs stay in §3–§6, marked as not built.
