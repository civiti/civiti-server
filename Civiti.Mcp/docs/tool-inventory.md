# Civiti.Mcp — Tool, Resource, and Prompt Inventory

> **Status:** Living document. Section numbers are stable; new sections
> append to §8+.
> Last revision: 2026-04-21 — added §1 Public (anonymous) tools;
> corrected `search_issues` filter signature to match REST; phased rollout
> updated to ship public endpoint first.

## Guiding principles

1. **Curated, not exhaustive.** A small set of well-named tools with strong
   descriptions outperforms a 1:1 mirror of the REST API. Agents struggle
   when two tools overlap.
2. **Tools wrap services, not endpoints.** Each handler calls an existing
   method on `Civiti.Application` so business rules, moderation, and audit
   trails are identical to the REST path. No logic duplication.
3. **Every write tool has a rate-limit class and an audit tag.** The audit
   record stamps `Source = "mcp"` and the MCP tool name.
4. **No tool returns raw DB entities.** Responses are trimmed DTOs — large
   fields (long descriptions, base64 images) are summarized or replaced
   with resource URIs the agent can fetch on demand.
5. **Public tools mirror the public REST surface exactly.** If the REST
   endpoint is anonymous, its MCP counterpart is anonymous on the same
   terms (filters, rate limits, public-fields-only filtering).

## Endpoints

Two MCP endpoints are exposed:

- **`/mcp/public`** — no auth. Serves §1 Public tools. Rate-limited per source IP.
- **`/mcp`** — OAuth 2.1 bearer required (see [`auth-design.md`](auth-design.md)). Serves the §1 Public tools *with enriched authenticated responses* (e.g. `HasVoted` on issue lookups), plus §2–§4 tools scoped by the granted OAuth scope.

The same tool name (`search_issues`, `get_issue`, `list_authorities`, …) appears on both endpoints. The handler lives in `Civiti.Application`; `Civiti.Mcp` passes `currentUserId: null` on the public path and the resolved user on the authenticated path. No divergent logic.

## Rate-limit classes

| Class | Per-session | Per-user (global) | Notes |
| --- | --- | --- | --- |
| `read.public` | — | — | **30 / min per source IP.** Anonymous tools on `/mcp/public`. No user or session context. |
| `read.cheap` | 60 / min | 300 / min | Profile/leaderboard reads, cached. |
| `read.search` | 20 / min | 120 / min | Authenticated search, hits DB harder. |
| `write.citizen` | 6 / min | 20 / hour | Issue creation, voting, comments. |
| `write.admin` | 10 / min | 60 / hour | Approve/reject. Two-step confirm required. |
| `ai.claude` | Shared budget with REST `/enhance-text` (10 / min / user). | — | Runs through `ClaudeEnhancementService`. |

## 1. Public (anonymous) tools

Exposed on `/mcp/public`. No authentication. Rate-limited per source IP. They mirror the already-public surface of `Civiti.Api` one-for-one.

**Intent:** let an AI agent browse civic data and help a non-user draft a petition email without requiring signup. The citizen still composes and sends the petition from their own inbox; Civiti only counts it via `mark_email_sent`. This mirrors how the Civiti web UI works for unauthenticated visitors.

| Tool | Backing service | Input summary | Rate class |
| --- | --- | --- | --- |
| `search_issues` | `IssueService.GetAllIssuesAsync(request, null)` | `page?` (int, default 1), `pageSize?` (int, 1–100, default 12), `category?` (enum: Infrastructure, Environment, Transportation, PublicServices, Safety, Other), `urgency?` (enum: UrgencyLevel), `status?` (comma-separated list of IssueStatus; defaults to `Active`), `district?` (string), `address?` (string), `sortBy?` (`date`\|`popularity`\|`votes`\|`urgency`, default `date`), `sortDescending?` (bool, default `true`) | `read.public` |
| `get_issue` | `IssueService.GetIssueByIdAsync(id, null)` | `id: uuid` | `read.public` |
| `list_authorities` | `AuthorityService.ListForLocationAsync(city, district?)` | `city`, `district?` | `read.public` |
| `get_categories` | `StaticDataService.GetCategoriesAsync()` | none | `read.public` (cached) |
| `get_leaderboard` | `GamificationService.GetLeaderboardAsync(scope, city?, take)` | `scope: global\|city`, `city?` (required if `scope=city`), `take` | `read.public` |
| `mark_email_sent` | `IssueService.IncrementEmailCountAsync(id, clientIp)` | `id: uuid` | Bound by the existing service-layer rule: **1 / IP / issue / hour** (same as REST `POST /api/issues/{id}/email-sent`). |

### Filter surface and safety

- `search_issues` mirrors the REST `GET /api/issues` query contract exactly — no invented filters.
- Public tool responses return publicly visible data only: `Approved` / `Active` / `Resolved` issues (never `Pending` / `Rejected`), author PII stripped (display name allowed, email/phone never exposed), and no `HasVoted` field (no user context).
- These rules live in `IssueService.GetAllIssuesAsync` / `GetIssueByIdAsync` — the MCP tool does not re-implement them, so drift between REST-public and MCP-public is impossible.
- `clientIp` for `mark_email_sent` is taken from `HttpContext.Connection.RemoteIpAddress` via the existing `ForwardedHeaders` middleware (to be moved into `Civiti.Infrastructure` during the shared-library refactor).

## 2. Citizen tools

Exposed on `/mcp` with `civiti.read` or `civiti.write` scope. All §1 public tools are also available here, returning the same payloads **plus** user-contextual fields (e.g. `HasVoted` on issues). The tools below are the citizen-specific additions.

### 2.1 Read

| Tool | Scope | Backing service | Input summary | Rate class |
| --- | --- | --- | --- | --- |
| `get_my_profile` | `civiti.read` | `UserService.GetProfileAsync(userId)` | none | `read.cheap` |
| `get_my_gamification` | `civiti.read` | `GamificationService.GetForUserAsync(userId)` | none | `read.cheap` |
| `list_my_issues` | `civiti.read` | `IssueService.GetForUserAsync(userId, status?, take, skip)` | optional status filter, paging | `read.cheap` |
| `list_my_activity` | `civiti.read` | `ActivityService.GetForUserAsync(userId, take)` | paging | `read.cheap` |
| `list_my_blocked_users` | `civiti.read` | `BlockService.ListBlockedAsync(userId)` | none | `read.cheap` |

### 2.2 Write

| Tool | Scope | Backing service | Input summary | Rate class | Audit |
| --- | --- | --- | --- | --- | --- |
| `create_issue` | `civiti.write` | `IssueService.CreateAsync(dto)` | `title`, `description`, `category`, `urgencyLevel`, `address`, `lat`, `lon` | `write.citizen` | `Activity{Source=mcp, Tool=create_issue}` |
| `update_my_profile` | `civiti.write` | `UserService.UpdateProfileAsync(userId, dto)` | `displayName?`, `county?`, `city?`, `district?` | `write.citizen` | audit row |
| `vote_on_issue` | `civiti.write` | `IssueService.VoteAsync(userId, issueId, direction)` | `issueId`, `direction: up\|remove` | `write.citizen` | activity |
| `add_comment` | `civiti.write` | `CommentService.CreateAsync(userId, issueId, text)` | `issueId`, `text` | `write.citizen` | activity |
| `vote_on_comment` | `civiti.write` | `CommentService.VoteAsync(userId, commentId, direction)` | `commentId`, `direction` | `write.citizen` | activity |
| `report_content` | `civiti.write` | `ReportService.CreateAsync(userId, targetType, targetId, reason)` | `targetType: issue\|comment`, `targetId`, `reason`, `notes?` | `write.citizen` | report row |
| `block_user` | `civiti.write` | `BlockService.BlockAsync(userId, blockedId)` | `blockedUserId` | `write.citizen` | block row |
| `unblock_user` | `civiti.write` | `BlockService.UnblockAsync(userId, blockedId)` | `blockedUserId` | `write.citizen` | — |
| `mark_email_sent_authenticated` | `civiti.write` | `IssueService.IncrementEmailCountAsync(id, clientIp)` + `ActivityService.LogEmailSentAsync(userId, id)` | `issueId` | `write.citizen` | activity |

Note: `mark_email_sent` on `/mcp/public` is anonymous and only bumps the counter. The authenticated `/mcp` version (`mark_email_sent_authenticated`) additionally logs the activity against the user's profile for gamification / streak tracking.

### 2.3 Content-moderation note

`create_issue`, `update_my_profile` (display name), and `add_comment` send user text through the same OpenAI moderation pipeline `Civiti.Api` uses today. The tool result on rejection is a structured error (`{ok: false, reason: "moderation_rejected", category: ...}`) so the agent can explain the block to the user rather than retry.

## 3. Admin tools

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

MCP prompts are reusable templates the client can offer as commands in its UI. Proposed starter set:

| Prompt | Trigger |
| --- | --- |
| `/civiti-find-and-petition` *(public)* | Walks an anonymous user through `search_issues` → `get_issue` → drafts a petition email → reminds the user to send from their inbox → calls `mark_email_sent`. |
| `/civiti-report-issue` | Walks the authenticated user through `draft_issue_description` + `create_issue`. |
| `/civiti-my-dashboard` | Fetches `civiti://me`, `list_my_issues`, `list_my_activity` and summarizes. |
| `/civiti-review-queue` (admin) | Fetches `civiti://admin/pending-queue` and offers the agent a moderation workflow. |

Prompts are low priority for v1 — ship tools + resources first, iterate on prompts once we see how users actually interact.

## 7. Phased rollout

1. **v0 (public smoke test):** `/mcp/public` endpoint with §1 tools. No OAuth, no session. Simplest possible end-to-end validation against any MCP client (Claude Desktop, Cursor, etc.). Confirms transport, tool definitions, and service-layer shape before we add auth complexity.
2. **v1 (authenticated read):** `/mcp` endpoint with §2.1 Citizen read tools + OAuth flow against Civiti.Auth. Claude Desktop as the canonical test client.
3. **v2 (citizen write):** §2.2 Citizen write tools + `draft_issue_description` + §5 user-scoped resources.
4. **v3 (admin preview, gated):** §3 Admin tools with the two-step confirm flow. Opt-in only; comms to admin users first.
5. **v4:** Prompts (§6), broader client onboarding beyond the initial allow-list.

## 8. Open questions

1. **Naming conventions:** `create_issue` vs. `report_issue` vs. `submit_issue`. REST uses "create". Agents read these names directly — which is clearest to an LLM?
2. **Pagination style:** cursor-based (opaque token, ordering stable) vs. take/skip. REST uses take/skip (and we've mirrored it for `search_issues`); agents handle cursor tokens fine, but the divergence from REST adds cognitive load. Recommend: keep take/skip to stay aligned with REST; revisit if we hit ordering-instability bugs in practice.
3. **Bulk tools beyond admin:** is there value in `vote_on_issues (ids[])` for citizens, or does that invite spam? Recommend: no — keep citizen writes one-at-a-time.

## 9. Resolved decisions (log)

- **2026-04-21** — **Public (anonymous) MCP surface in scope for v1.** Mirrors the existing public REST endpoints (`GET /api/issues`, `GET /api/issues/{id}`, `GET /api/issues/{id}/poster`, `POST /api/issues/{id}/email-sent`). Exposed on a dedicated `/mcp/public` endpoint with IP-based rate limiting. Petition-sending is still citizen-originated from their own inbox; Civiti only counts via `mark_email_sent`.
- **2026-04-21** — **`search_issues` filter signature aligned with REST.** Filters: `page`, `pageSize` (1–100), `category`, `urgency`, `status` (comma-separated), `district`, `address`, `sortBy`, `sortDescending`. No invented `city`, `county`, or `query` params; if we want those, they go on the REST endpoint first.
- **2026-04-21** — **Rollout order: public first, authenticated second.** Public endpoint is the simpler smoke test (no OAuth dependencies) and validates the transport + service-wrapping shape before we layer auth on top.
