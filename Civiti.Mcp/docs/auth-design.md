# Civiti.Mcp — Authentication & Authorization Design

> **Status:** Living document. Updated as decisions are made.
> See the "Resolved decisions" log at the end for revision history.
> Anything marked **Not built** records intent, not running behaviour. For URLs,
> rate limits and troubleshooting, see the [technical reference](../README.md).

## 1. The constraint

Civiti users authenticate against Supabase. Supabase is our identity
provider — not an OAuth 2.1 authorization server for third-party apps.

MCP clients (Claude Desktop, Claude Code, Cursor, ChatGPT connectors) expect
an OAuth 2.1 authorization flow with PKCE and discovery, per the MCP auth
spec. They don't know how to obtain a Supabase session directly.

**Design task:** expose OAuth 2.1 for MCP clients while keeping Supabase as
the single source of truth for "who is a Civiti user?".

## 2. Architecture: three services

```
  MCP client                         Civiti.Auth                Supabase
(Claude Desktop)  ────OAuth 2.1────►   (OpenIddict)  ──login flow──►
                                            │
                                     mints Civiti tokens
                                            │
                                            ▼
                              Civiti tokens held by client
                                            │
  MCP client       ──Bearer access token──►  Civiti.Mcp   ──service calls──► DB
                                          (Resource Server)
```

Three .NET services in one solution, referencing the shared class libraries
(see [`architecture.md`](architecture.md)):

| Service | Role | Why separate |
| --- | --- | --- |
| **Civiti.Auth** | OAuth 2.1 Authorization Server. Owns `/authorize`, `/token`, consent UI, client registry, session lifecycle. Delegates user login to Supabase. | Standard OAuth separation of AS from RS ([RFC 9728](https://datatracker.ietf.org/doc/html/rfc9728)). Reusable for future third-party clients (mobile SDK, partner integrations). |
| **Civiti.Mcp** | Resource Server. Validates bearer tokens, hosts MCP tools. | Keeps the tool-handling surface focused. Token validation is one `AddOpenIddict().AddValidation(...)` call. |
| **Civiti.Api** | Unchanged. Continues to validate Supabase JWTs directly for its own (Angular, mobile) clients. | Out of scope for MCP. The existing auth path stays undisturbed. |

`Civiti.Mcp` publishes `/.well-known/oauth-protected-resource` (RFC 9728)
pointing at `Civiti.Auth`. MCP clients discover the AS from there and
proceed normally.

### Two MCP endpoints

`Civiti.Mcp` exposes two transport endpoints:

- **`/mcp`** — OAuth 2.1 bearer required on every request.
- **`/mcp/public`** — no auth. Lists the same tools, but those that need a token fail closed, so only the public ones work (see [`tool-inventory.md` §1](tool-inventory.md#1-public-anonymous-tools)). IP-based rate limiting. No session, no consent flow, no OAuth discovery.

The public endpoint exists to mirror the already-public surface of `Civiti.Api` (`GET /api/issues`, `GET /api/authorities`, `POST /api/issues/{id}/email-sent`, …) so AI agents can help unauthenticated users browse civic data and draft petitions without requiring signup. Everything in this document from §3 onward applies to `/mcp` only.

## 3. Why OpenIddict

OpenIddict is the battle-tested OAuth 2.1 / OIDC stack for ASP.NET Core.
We lean on it for most of the protocol surface:

- RFC 6749 / OAuth 2.1 (authorization code flow, refresh tokens)
- RFC 7636 (PKCE) — required for every client
- RFC 8414 (AS metadata)
- RFC 9068 (JWT access token profile)
- RFC 7009 (revocation)
- Application and scope management via `IOpenIddictApplicationManager`

Introspection (RFC 7662) is not enabled: `Civiti.Mcp` validates JWTs itself.

**Our code = thin layer on top:**

- Login and consent UI (two Razor Pages).
- Login delegation: `/authorize` → `/Login` → Supabase → map the Supabase user to a Civiti principal → issue tokens.
- RFC 7591 Dynamic Client Registration. OpenIddict 7.5 has no built-in DCR, so `/register` is hand-written (§6).
- RFC 9728 Protected Resource Metadata, served by `Civiti.Mcp`.
- Client allow-list seeded at startup.

No hand-rolled crypto and no custom token formats.

### Token format

Civiti.Auth issues **JWT access tokens** (RFC 9068, 15-minute TTL) signed by
its own JWKS; `Civiti.Mcp` validates them statelessly. **Refresh tokens are
self-contained and encrypted**, rotating on every use, with a row in
`OpenIddictTokens` tracking each one's status. JWT for access gives us zero DB
round-trips on tool calls; the status row gives us immediate server-side
revocation of the refresh token.

The refresh-token lifetime is 30 days and it slides: each rotation issues a new
token that lasts another 30 days, because OpenIddict's sliding expiration is left
on. A client that refreshes at least once every 30 days stays connected. One that
stays idle for longer has to sign in again.

Because a refresh token carries its own encrypted payload, the keys must
survive restarts: they load from `OIDC_SIGNING_KEY` and `OIDC_ENCRYPTION_KEY`
(see `docs/technical/configuration-guide.md`).

## 4. Trust boundary: Civiti.Auth is the identity edge

**We do not persist Supabase refresh tokens.** Once `Civiti.Auth` mints its
own tokens, the upstream Supabase session is forgotten.

### Login

1. MCP client hits `/authorize` on `Civiti.Auth`.
2. Without a session cookie, the user lands on `/Login` and signs in with email and password (Supabase password grant) or Google (Supabase PKCE, returning through `/supabase-callback`).
3. `Civiti.Auth` validates the resulting Supabase JWT, extracts `sub` and the role, confirms a matching `UserProfile` row exists, sets a 30-minute session cookie and discards the Supabase JWT. There is no auto-provisioning: without a profile, login stops with a `403`.
4. If no stored consent covers the requested scopes, the consent screen runs (see §7).
5. Civiti access token (JWT, 15 min) + refresh token (encrypted, rotating, 30 d) are minted and returned.

### Refresh

- Client presents the Civiti refresh token.
- OpenIddict decrypts it and checks its status row: not revoked, not already redeemed, not expired.
- `Civiti.Auth` **re-validates the user** by querying the Supabase Admin API. A missing or banned user gets `invalid_grant`, and so does any transient Supabase failure, because the refresh fails closed. The call needs `SUPABASE_SERVICE_KEY`, which holds the Supabase secret key; without it, the lookup returns nothing and every refresh is denied.
- The role claim is updated from Supabase, and `AdminScopeFilter` strips admin scopes the user no longer qualifies for.
- New access + refresh tokens issued; old refresh token marked redeemed.
- `McpSessionWriteHandler` repoints `McpSessions.OpenIddictTokenId` at the new refresh token and bumps `LastSeenAt`, from inside OpenIddict's sign-in pipeline.

No Supabase refresh token is ever held at rest. The authoritative user record lives in Supabase; we only ever cache what we need for the current token's lifetime.

### Role changes and upstream revocation

A user who loses `admin` in Supabase must not keep admin-scoped MCP sessions. The layered defenses:

- **Short access tokens (15 min).** Role claim is stamped at issue time; the worst-case stale-admin window is capped by the access-token TTL.
- **Every refresh re-validates role** against the Supabase Admin API. Cheap — refreshes are rare.
- **Background sweep every 5 min** re-validates every active session carrying admin scopes against Supabase, and revokes it if the user is gone, banned, or no longer `admin`. A transient Supabase failure skips that session, and a missing `SUPABASE_SERVICE_KEY` skips the whole pass, so an outage can't mass-revoke sessions.
- **Deferred:** Supabase auth webhooks → push-based revocation on role change / user disable. Adds operational moving parts; defer until we see a case where 5-min latency is unacceptable.

## 5. Session storage

OpenIddict provides the token schema out of the box. We add two tables:
`McpSessions`, a view of connected clients, and `McpUserClientPreferences`, which
stores consent per user and client (§7).

```
McpSessions
────────────────────────────────────────────────────────────────
Id                  uuid   PK
OpenIddictTokenId   text   (nullable) id of the current refresh token
ClientId            text   OpenIddict client id
SupabaseUserId      text
ScopesGranted       text[]
CreatedAt           timestamptz
LastSeenAt          timestamptz
RevokedAt           timestamptz (nullable)
RevokedReason       text (nullable)
```

All token hashing, issuance, rotation, and revocation are handled by OpenIddict's built-in stores — we do not re-implement.

### `McpSessions` lifecycle sync

OpenIddict's own tables remain the source of truth for token state. `McpSessions` is kept in step at two event points:

- **On token rotation:** `McpSessionWriteHandler` updates `OpenIddictTokenId` and bumps `LastSeenAt`. See §4's Refresh subsection.
- **On revocation:** `McpSessionRevokeHandler` sets `RevokedAt` with reason `user_revoked` after OpenIddict has revoked the token, and the sweep in §4 sets it with `role_lost`, `user_banned` or `user_disabled`.

**Not built:** nothing marks a session when its refresh token simply expires, so `WHERE RevokedAt IS NULL` is not yet a reliable "active sessions" query.

## 6. Client identification and allow-list

### How MCP clients are identified

Public clients cannot be cryptographically authenticated — there is no
client secret, no code signature we can verify. The security boundary is:

- **`client_id`** — `/register` generates it, or the allow-list fixes it. Users never handle it.
- **`redirect_uri`** — must match a URI registered against the `client_id`. This is the enforcing gate: even if a malicious app claims a legitimate `client_id`, the authorization code flows back to the registered redirect URI. Loopback URIs match on any port (RFC 8252); everything else matches exactly.
- **PKCE** — binds the authorization code to the specific client instance that initiated the flow. Mandatory.
- **Consent screen** — shows `client_name` prominently so the user catches impersonation.

This is the same security model every major third-party OAuth provider uses for native clients.

### Allow-list

`ClientAllowListSeeder` writes these OpenIddict application entries at startup:

| `client_id` | Display name | Redirect URI | Scopes | `allowsAdminScopes` |
| --- | --- | --- | --- | --- |
| `claude-desktop` | Claude Desktop | `http://127.0.0.1:0/callback` (any port) | `civiti.read`, `civiti.write`, `civiti.admin.read`, `civiti.admin.write` | Yes |
| `claude-code` | Claude Code | `http://127.0.0.1:0/callback` (any port) | `civiti.read`, `civiti.write`, `civiti.admin.read`, `civiti.admin.write` | Yes |
| `claude-ai` | Claude (claude.ai) | `https://claude.ai/api/mcp/auth_callback` | `civiti.read`, `civiti.write` | No |
| `cursor` | Cursor | `cursor://anysphere.cursor-retrieval/callback` | `civiti.read`, `civiti.write` | No |
| `chatgpt-connector` | ChatGPT | `https://chatgpt.com/connector_platform_oauth_redirect` | `civiti.read` | No |

A client only reaches one of these entries if it is configured with that exact `client_id`. Connector flows register through DCR instead and get a generated `client_id`, as claude.ai does in production. The `cursor` and `chatgpt-connector` redirect URIs have not been confirmed against those vendors, and neither URI is accepted by DCR. See [Supported clients](../README.md#supported-clients).

### Admin-scope gating

Each allow-list entry carries a `civiti.allows_admin_scopes` property. **Only `claude-desktop` and `claude-code` have it set.** `AdminScopeFilter` strips `civiti.admin.*` unless the client has that property **and** the user's Supabase role is `admin`. It runs at `/authorize`, on the consent page (render and POST) and on every refresh.

`Program.cs` calls `IgnoreScopePermissions()`, because real clients request scopes they weren't registered for and OpenIddict would otherwise reject them. That makes `AdminScopeFilter` the only per-client scope gate. Any change to it, its call sites or that option has to be reviewed together, and a new privileged scope must be added to `AdminScopeFilter.AdminScopes`.

No tool consumes an admin scope yet (see §9).

### Dynamic Client Registration (RFC 7591)

**DCR is ON.** `RegisterEndpoint.cs` is anonymous and bounded by these guardrails:

- **Redirect URIs.** Each must be a loopback URI, on `127.0.0.1` or `::1` with any port and path ([RFC 8252](https://datatracker.ietf.org/doc/html/rfc8252) native-app pattern), or an exact match in `LoopbackRedirectUriMatcher.AllowlistedDcrRedirectUris`. That list holds one entry, `https://claude.ai/api/mcp/auth_callback`, because claude.ai relays the code through its own servers. Anything else, including `localhost`, is rejected with `invalid_redirect_uri`. Every entry on that list must be a host the vendor controls: a host an attacker controls could complete the code flow and take the tokens.
- **Loopback port wildcard.** OpenIddict matches `redirect_uri` exactly, but native apps bind ephemeral ports. `LoopbackAwareAuthorizationRedirectUriValidator` and `LoopbackAwareTokenRedirectUriValidator` replace the built-in validators and relax the port only; scheme, path, query and fragment must still match.
- **Size limits.** At most 10 redirect URIs, and a `client_name` of at most 200 characters.
- **Per-IP rate limit.** 20 requests per source IP per 24 hours, in a fixed window. Rejected registrations count too.
- **Scope ceiling.** Registered clients get `civiti.read` and `civiti.write` at most. `civiti.admin.*` is dropped silently, an empty request gets both, and a request with neither is rejected with `invalid_client_metadata`. `offline_access` is always added, so refresh works.
- **Public clients.** No client secret, PKCE required, explicit consent.
- **Trust badge.** Allow-listed clients show "Verified" on the consent screen and registered clients show "Unverified", with `client_name` shown in both cases.

Why DCR is on: every practical remote-MCP deployment (Linear, Atlassian, GitHub, Sentry, …) enables DCR because clients expect it. A Civiti user pasting the MCP URL into a client should not also have to paste a `client_id` — the client registers itself automatically, and we enforce the safety boundary at the redirect-URI and scope layer instead.

### User-facing setup

End-user instructions live on [civiti.ro/ghid](https://civiti.ro/ghid). [`connecting-civiti-to-claude.md`](connecting-civiti-to-claude.md) has a developer summary.

## 7. Consent screen

Razor Page inside `Civiti.Auth`, reached from `/authorize` when no stored consent covers the requested scopes. Shows:

- `client_name` (from the registered application).
- **Trust badge**: "Verified" for allow-listed clients; "Unverified" for DCR-registered clients.
- The requested scopes in plain language.
- Any scopes that were requested but can't be granted.
- The privacy notice.
- Approve and Deny buttons.

**Consent is all-or-nothing.** There are no per-scope toggles and no remember checkbox. Approve always stores a `McpUserClientPreferences` row for that user and client, unioned with any earlier grant, so a later `/authorize` for the same scopes skips this page. Deny signs the user out of the Civiti.Auth session and returns `access_denied` to the client. DCR issues a new `client_id` on each registration, so re-adding a connector asks for consent again.

The page is in English only.

### Consent cookie

`/authorize` passes the OAuth context to `/Consent` in `civiti_auth_consent_ctx`, a cookie encrypted with Data Protection. It is `HttpOnly`, limited to `Path=/Consent`, valid for 10 minutes and used once. A nonce in the form ties each submit to its cookie, so two tabs can't approve each other's client.

**It must stay `SameSite=Lax`.** Browsers decide whether to send a `SameSite` cookie from the site that started the navigation, and a same-site redirect partway through doesn't reset that. Every real consent flow starts elsewhere: claude.ai's Connect button, or Google → Supabase → `/supabase-callback`. Under `Strict`, the cookie never reaches `/Consent`, and every first-time consent fails with "Consent context missing or expired". That was the regression fixed in #161. `Lax` still withholds the cookie from cross-site POSTs, so the Approve and Deny submits stay protected, on top of the antiforgery token and the nonce. The session and PKCE cookies are `Lax` for the same reason.

### Copy ownership

Engineering owns the scaffold, the string keys, and the layout. **Final copy, in particular the privacy notice, is owned by product/legal.** The notice in production is still labelled as a placeholder, and it tells users they can revoke access in their account settings, which they can't yet (see §10 and §11).

## 8. Scopes

| Scope | Grants |
| --- | --- |
| `civiti.read` | Read-only citizen tools. |
| `civiti.write` | Citizen write tools. |
| `offline_access` | Issue a refresh token. |
| `civiti.admin.read` | Issuable to allow-listed clients for Supabase `admin` users. No tool uses it yet. |
| `civiti.admin.write` | Same as `civiti.admin.read`. |

`civiti.read`, `civiti.write` and `offline_access` are advertised in discovery. The admin scopes are deliberately not, because some clients would then request them and have them stripped.

`Civiti.Mcp` enforces scopes in the tool handlers, not in endpoint policies. `/mcp` only requires an authenticated principal; each tool then calls `IMcpCitizenContext`, which reads the validated token's scope claims with no additional lookups and returns a structured error if the scope is missing.

> *Notation:* later sections use `civiti.admin.*` in prose as shorthand for "both `civiti.admin.read` and `civiti.admin.write`". **The string `civiti.admin.*` is never a literal scope value.** OpenIddict matches scopes by exact equality — allow-list rows, DCR requests, token claims, and enforcement checks always use the fully-qualified scope names.

## 9. Admin-specific hardening

> **Mostly not built.** No admin tool exists, so nothing below is reachable today. `UserProfile.McpAdminAccessEnabled`, the two-step confirm, the kill-switch and the audit emails do not exist. What does exist is `AdminScopeFilter` (§6), the role sweep (§4), and the content quarantine in item 4, which covers citizen tools today.

See [`tool-inventory.md` §3](tool-inventory.md#3-admin-tools) for the two-step confirm mechanism.

1. **Opt-in per admin.** `UserProfile.McpAdminAccessEnabled` must be `true`. Default `false`. Set via the existing admin UI, never via MCP. **Flipping the flag from `true` to `false` is a kill-switch**: the write path in the admin UI immediately revokes every active MCP session carrying `civiti.admin.*` scopes for that user, so the stale-admin window on disable is zero (not the 5-minute sweep window). The background sweep in §4 is a belt-and-braces fallback — it also checks `McpAdminAccessEnabled`, not only the Supabase role — in case the kill-switch write path is ever bypassed (e.g. direct DB mutation during an incident).

   *Implementation mechanism:* the kill-switch lives on Civiti.Api's admin-UI write path. Civiti.Api registers OpenIddict's Core services (`AddOpenIddict().AddCore().UseEntityFrameworkCore()` against the shared `CivitiDbContext`) — this gives it `IOpenIddictTokenManager` and `IOpenIddictAuthorizationManager` without running the full Server stack. On flag flip, the handler enumerates tokens where `SubjectId == userId` and the authorization carries any `civiti.admin.*` scope, then calls `TryRevokeAsync` on each. No inter-service HTTP call is needed, because the OpenIddict stores are backed by the same Postgres instance all three hosts share. Civiti.Auth's RFC 7009 `/revoke` endpoint remains the canonical public revocation path for MCP clients but is not the mechanism used by this internal kill-switch.
2. **Stricter rate limits** on admin write tools.
3. **Two-step confirm for destructive actions.** Defense against prompt injection: an LLM context poisoned by user-generated content being moderated cannot mutate state on its own — the admin must explicitly invoke `confirm_admin_action(id)` in a fresh turn.
4. **Content quarantine in tool results.** User-submitted text returned to an admin agent is wrapped with clear delimiters and a "this content is untrusted" note.
5. **Audit alerts.** Every admin-scoped tool call emails the admin: "`approve_issue` on issue #1234 executed via Claude Desktop at 14:03. Revoke access at …".

## 10. Revocation

- **By the client:** `POST /revoke` (RFC 7009). OpenIddict revokes the token, and `McpSessionRevokeHandler` sets `McpSessions.RevokedAt`. Discovery lists only confidential-client methods for this endpoint, so a client that reads that list strictly may not call it.
- **Automatic:**
  - On refresh, if the Supabase user is gone or banned, the refresh is denied. A changed role updates the claim and strips admin scopes.
  - The 5-minute sweep revokes sessions carrying admin scopes when the user is gone, banned, or no longer `admin` (§4).

**Revoking stops refreshes, not access tokens.** `Civiti.Mcp` validates JWTs without calling `Civiti.Auth`, so an issued access token keeps working for up to 15 minutes.

**Not built:** a "Connected AI Assistants" page for users, an admin kill-switch, and automatic revocation on `UserProfile` soft-delete. Removing a connector in a client doesn't necessarily call `/revoke`, so today a user has no Civiti-side way to end a session.

## 11. Open questions

1. **Revocation for users.** The consent notice promises revocation in account settings, but no such page exists in `civiti-web`, and `Civiti.Api` exposes no MCP session data for one to read.
2. **Privacy notice.** The production text is still a placeholder awaiting product/legal review (§7).

## 12. Resolved decisions (log)

- **2026-04-21** — Split `Civiti.Auth` from `Civiti.Mcp` (AS vs RS per RFC 9728).
- **2026-04-21** — Do not persist Supabase refresh tokens. `Civiti.Auth` is the identity edge; we re-validate the Supabase user on Civiti-side refresh via the Admin API and forget.
- **2026-04-21** — Allow-list only for v1; Dynamic Client Registration endpoint is not exposed. *(Superseded later the same day — see DCR entry below.)*
- **2026-04-21** — OpenIddict is the AS and token-validation stack.
- **2026-04-21** — **Anonymous MCP surface in scope for v1 via a dedicated `/mcp/public` endpoint.** (Supersedes an earlier "out of scope" decision made the same day.) Rationale: the Civiti REST API already exposes an anonymous public surface (issue list/detail, authority directory, `email-sent` counter); the MCP path must mirror it for parity. Concrete use case: an AI agent helping a non-signed-up user browse issues, draft a petition email, and bump the email-sent counter — the petition itself is still citizen-originated from their own inbox. Sibling endpoint, IP-based rate limiting, no session or OAuth flow. Tool list defined in [`tool-inventory.md` §1](tool-inventory.md#1-public-anonymous-tools). Everything else in this document applies to the authenticated `/mcp` endpoint only.
- **2026-04-21** — **Token format: JWT access (15 min) + opaque rotating refresh (30 d).** JWT access → zero DB round-trip in `Civiti.Mcp` (validates via JWKS). Opaque refresh → server-side revocation is immediate. Industry default; one config switch in OpenIddict. *(Corrected by the 2026-11-05 entry: the switch was never enabled.)*
- **2026-04-21** — **Device Authorization Grant (RFC 8628) deferred.** All v1 allow-listed clients support browser redirect (loopback or claimed URL scheme). Add if and when a client that cannot open a browser is onboarded.
- **2026-04-21** — **Admin scopes (`civiti.admin.*`) restricted to `claude-desktop` and `claude-code` in v1.** Enforced via `allowsAdminScopes` flag on allow-list entries (§6). Other clients get the flag on a case-by-case review. Easy to expand; expensive to walk back once an admin has granted a wider client admin access.
- **2026-04-21** — **Consent-screen copy ownership: product/legal.** Engineering ships the scaffold; the GDPR notice wording is a compliance artefact, not an engineering decision. Blocking item for v1 launch.
- **2026-04-21** — **Supabase auth webhooks for push-based revocation deferred.** Refresh-time re-validation (~every access-token TTL for active users) plus the 5-minute background sweep for admin-scoped sessions (§4) bound the stale-state window acceptably for v1. Revisit if (a) stale-admin windows become a compliance concern, (b) Supabase offers webhooks with auth acceptable to our threat model, or (c) we observe refresh-time latency becoming user-visible.
- **2026-04-28** — **v1c PR 1: JWT-only validation on Civiti.Mcp** (no introspection, no shared OpenIddict.Core dependency on the Mcp host). Refresh-token revocation propagates to access tokens within the 15-min access-token TTL — acceptable for citizen reads. Admin scopes get stronger guarantees via the refresh-time re-validation and the 5-min sweep on Civiti.Auth (see §4); they don't need RS-side introspection on top. If a future tool surfaces a stricter latency requirement, switch on OpenIddict introspection without touching tokens.
- **2026-04-28** — **Two env vars carry the trust boundary on Civiti.Mcp.** `CIVITI_AUTH_ISSUER` is the authoritative AS URL (drives JWKS fetch + `iss` check); `CIVITI_MCP_PUBLIC_ORIGIN` is the canonical scheme+host this Mcp deploy is reachable on (drives the `resource` value in the RFC 9728 discovery doc and the `resource_metadata` parameter in WWW-Authenticate). Mirrors the same `CIVITI_AUTH_PUBLIC_ORIGIN` defense Civiti.Auth uses against spoofed Host headers — never trust `Request.Host`.
- **2026-04-21** — **Dynamic Client Registration enabled with guardrails.** (Supersedes the earlier "DCR off" decision made the same day.) Rationale: every practical remote-MCP deployment uses DCR because clients expect it, and with the UX worked out end-to-end a Civiti user pasting the MCP URL into Claude Desktop must not also have to paste a `client_id`. Safety shifts from "who can register" to "what redirect URIs are accepted and what scopes can be granted". Policy (§6): loopback-only redirect URIs (RFC 8252), per-IP rate limit on `/register`, DCR-registered clients capped at `civiti.read` + `civiti.write` (never admin), admin scopes stay gated on the pre-registered allow-list via `allowsAdminScopes`, consent screen displays a Verified/Unverified badge.
- **2026-11-05** — **Persist the OpenIddict keys; refresh tokens are self-contained, not reference tokens.** The 2026-04-21 token-format entry assumed `UseReferenceRefreshTokens()`, which was never enabled, so refresh tokens were encrypted with an ephemeral key and every Civiti.Auth restart signed out every MCP client (seen in production as `invalid_grant` / ID2004 a minute after a deploy). The keys now load from `OIDC_SIGNING_KEY` and `OIDC_ENCRYPTION_KEY`, required outside Development. Reference tokens stay off: they would not have fixed this, because the stored payload is still encrypted with the same key (a refresh after restart with reference tokens on and ephemeral keys was still rejected), and revocation already works through the `OpenIddictTokens` status row.
- **2026-11-06** — **Reconciled this document with the running code.** Changes that had shipped without being recorded here: DCR also accepts the claude.ai relay callback, not only loopback URIs; consent is all-or-nothing, with no toggles or remember checkbox; the refresh-token lifetime slides rather than being absolute. Planned work that has not shipped is now marked as such: the admin hardening in §9, the "Connected AI Assistants" page, expiry tracking on `McpSessions`, and introspection.
