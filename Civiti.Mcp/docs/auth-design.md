# Civiti.Mcp — Authentication & Authorization Design

> **Status:** Living document. Updated as decisions are made.
> See the "Resolved decisions" log at the end for revision history.

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

- **`/mcp`** — OAuth 2.1 bearer required. All authenticated tools and resources (citizen, admin).
- **`/mcp/public`** — no auth. Serves only the public tool subset (see [`tool-inventory.md` §1](tool-inventory.md#1-public-anonymous-tools)). IP-based rate limiting. No session, no consent flow, no OAuth discovery.

The public endpoint exists to mirror the already-public surface of `Civiti.Api` (`GET /api/issues`, `GET /api/authorities`, `POST /api/issues/{id}/email-sent`, …) so AI agents can help unauthenticated users browse civic data and draft petitions without requiring signup. Everything in this document from §3 onward applies to `/mcp` only.

## 3. Why OpenIddict

OpenIddict is the battle-tested OAuth 2.1 / OIDC stack for ASP.NET Core.
We lean on it because it gives us the entire protocol surface for free:

- RFC 6749 / OAuth 2.1 (authorization code flow, refresh tokens)
- RFC 7636 (PKCE) — enforced per client
- RFC 8414 (AS metadata) and RFC 9728 (Protected Resource Metadata)
- RFC 9068 (JWT access token profile) or opaque reference tokens
- RFC 7662 (introspection), RFC 7009 (revocation)
- RFC 7591 (Dynamic Client Registration) — **on, with guardrails** (loopback-only redirects, per-IP rate limit, admin scopes gated by pre-registered allow-list)
- Application and scope management via `IOpenIddictApplicationManager`

**Our code = thin layer on top:**

- Consent UI (one Razor Page).
- Login delegation: accept `/authorize` → redirect to Supabase-hosted login → on return, map Supabase user → Civiti principal → issue tokens.
- Client allow-list seeded at startup.
- Session-revocation UI (web app) hitting OpenIddict's revocation endpoint.

No hand-rolled crypto, no custom token formats, no bespoke endpoints.

### Token format

Civiti.Auth issues **JWT access tokens** (RFC 9068, 15-minute TTL) signed by
its own JWKS; `Civiti.Mcp` validates them statelessly. **Refresh tokens are
opaque** 256-bit reference values, stored hashed, rotating on every use
(30-day absolute TTL). JWT for access gives us zero DB round-trips on tool
calls; opaque refresh gives us immediate server-side revocation. This is
the textbook OAuth 2.1 split — one configuration switch in OpenIddict.

## 4. Trust boundary: Civiti.Auth is the identity edge

**We do not persist Supabase refresh tokens.** Once `Civiti.Auth` mints its
own tokens, the upstream Supabase session is forgotten.

### Login

1. MCP client hits `/authorize` on `Civiti.Auth`.
2. User is redirected to the Supabase-hosted login (or whatever auth method they use — Google OAuth via Supabase, email/password, etc.).
3. On return, `Civiti.Auth` has a short-lived Supabase JWT. It extracts `sub` (Supabase user id) and `app_metadata.role`, confirms a matching `UserProfile` row exists, and immediately discards the Supabase JWT.
4. Consent screen (see §7).
5. Civiti access token (JWT, 15 min) + refresh token (opaque, rotating, 30 d) are minted and returned.

### Refresh

- Client presents the Civiti refresh token.
- OpenIddict validates rotation state (hash match, not revoked, not expired).
- `Civiti.Auth` **re-validates the user** by querying the Supabase Admin API: does `SupabaseUserId` still exist, and what is the current role? If disabled / missing → revoke the session.
- New access + refresh tokens issued; old refresh token marked consumed.

No Supabase refresh token is ever held at rest. The authoritative user record lives in Supabase; we only ever cache what we need for the current token's lifetime.

### Role changes and upstream revocation

A user who loses `admin` in Supabase must not keep admin-scoped MCP sessions. The layered defenses:

- **Short access tokens (15 min).** Role claim is stamped at issue time; the worst-case stale-admin window is capped by the access-token TTL.
- **Every refresh re-validates role** against the Supabase Admin API. Cheap — refreshes are rare.
- **Background sweep every 5 min** re-validates both the Supabase role **and** `UserProfile.McpAdminAccessEnabled` for every active session carrying admin scopes. Either mismatch → revoke. See §9 for the admin-UI kill-switch path that revokes immediately and makes the sweep a fallback, not the primary mechanism. The same scheduled job also garbage-collects expired rows from `McpPendingAdminActions` (see [`tool-inventory.md` §3.2](tool-inventory.md#pending-action-storage) — rows older than 24 h past `ExpiresAt`).
- **Deferred:** Supabase auth webhooks → push-based revocation on role change / user disable. Adds operational moving parts; defer until we see a case where 5-min latency is unacceptable.

## 5. Session storage

OpenIddict provides the token schema out of the box. We add one ancillary
table for the UI that lists "your connected AI assistants":

```
McpSessions
────────────────────────────────────────────────────────────────
Id                  uuid   PK
OpenIddictTokenId   text   FK → OpenIddict refresh-token entry
ClientId            text   FK → OpenIddict applications
SupabaseUserId      uuid   FK → UserProfile
ScopesGranted       text[]
CreatedAt           timestamptz
LastSeenAt          timestamptz
RevokedAt           timestamptz (nullable)
```

All token hashing, issuance, rotation, and revocation are handled by
OpenIddict's built-in stores — we do not re-implement.

## 6. Client identification and allow-list

### How MCP clients are identified

Public clients cannot be cryptographically authenticated — there is no
client secret, no code signature we can verify. The security boundary is:

- **`client_id`** — we assign it. The user pastes it into their MCP client's config alongside the Civiti server URL.
- **`redirect_uri`** — must exact-match a string registered against the `client_id`. This is the enforcing gate: even if a malicious app claims a legitimate `client_id`, the authorization code flows back to the registered redirect URI, which the user's OS has already bound to the real app (URL scheme registration or loopback listener).
- **PKCE** — binds the authorization code to the specific client instance that initiated the flow. Mandatory.
- **Consent screen** — shows `client_name` prominently so the user catches impersonation.

This is the same security model every major third-party OAuth provider uses for native clients.

### Allow-list (v1)

Maintained as OpenIddict application entries, seeded from config at startup. Illustrative:

| `client_id` | `client_name` | `redirect_uris` | allowed scopes |
| --- | --- | --- | --- |
| `claude-desktop` | Claude Desktop | `http://127.0.0.1:*/callback` | `civiti.read`, `civiti.write`, `civiti.admin.read`, `civiti.admin.write` |
| `claude-code` | Claude Code | `http://127.0.0.1:*/callback` | `civiti.read`, `civiti.write`, `civiti.admin.read`, `civiti.admin.write` |
| `cursor` | Cursor | `cursor://anysphere.cursor-retrieval/callback` | `civiti.read`, `civiti.write` |
| `chatgpt-connector` | ChatGPT | `https://chatgpt.com/connector_platform_oauth_redirect` | `civiti.read` |

Actual values to be confirmed from each client's published docs before launch.

### Admin-scope gating

Each allow-list entry carries an `allowsAdminScopes` boolean. **For v1, only `claude-desktop` and `claude-code` carry the flag.** These are first-party Anthropic clients with well-engineered consent UIs and strong auditability; they are the safest place to land the admin surface initially. Requests for `civiti.admin.*` scopes from any other client are stripped before the consent screen renders (the user never sees an admin toggle they cannot grant). Expansion to other clients is gated on a per-client review.

### Dynamic Client Registration (RFC 7591)

**DCR is ON**, bounded by these guardrails:

- **Loopback redirects only.** `redirect_uris` must match `http://127.0.0.1:*/callback` or `http://[::1]:*/callback` ([RFC 8252](https://datatracker.ietf.org/doc/html/rfc8252) native-app pattern). Non-loopback URIs are rejected at registration. This binds the authorization code return to the user's own machine and is what every native Claude client uses.

  *Implementation note:* OpenIddict performs exact-string matching on `redirect_uri` by default — it does not treat `*` as a port wildcard. RFC 8252 §8.3 requires loopback URIs to treat the port as a don't-care value, since native apps bind to ephemeral ports. We implement this with a custom `IOpenIddictServerHandler` (or an `OpenIddict.Server.AspNetCore` event handler) that strips the port from loopback URIs before matching. Several community samples exist; pick one during implementation. This is the only non-trivial OpenIddict customization the design requires.
- **Per-IP rate limit** on the `/register` endpoint (default: 20 registrations / IP / day).
- **Scope ceiling for DCR-registered clients.** Automatically-registered clients may request `civiti.read` and `civiti.write` only. `civiti.admin.*` is **never** grantable to a DCR-registered client, regardless of the user's underlying role.
- **Allow-list gates admin scopes.** Only pre-registered entries carrying the `allowsAdminScopes` flag can request admin scopes (currently `claude-desktop` and `claude-code`). Allow-listed entries are seeded from config at startup with fixed `client_id`s.
- **Consent screen distinguishes trust level.** Allow-listed clients display a "Verified" badge; DCR-registered clients display an "Unverified" warning. In both cases `client_name` is shown prominently.

Why DCR is on: every practical remote-MCP deployment (Linear, Atlassian, GitHub, Sentry, …) enables DCR because clients expect it. A Civiti user pasting `https://mcp.civiti.app/mcp` into Claude Desktop should not also have to paste a `client_id` string from our docs — the client registers itself automatically, we enforce the safety boundary at the redirect-URI and scope layer instead.

### User-facing setup

End-user instructions live in [`connecting-civiti-to-claude.md`](connecting-civiti-to-claude.md). For v1 the only documented clients are Anthropic's: Claude Desktop, Claude Code, claude.ai.

## 7. Consent screen

Razor Page inside `Civiti.Auth`, rendered during `/authorize`. Shows:

- `client_name` (from the registered application).
- **Trust badge**: "Verified" for allow-listed first-party clients; "⚠️ Unverified" for DCR-registered clients. Localized.
- Requested scopes, plain-language, in Romanian + English.
- Per-scope opt-out toggles (user can strip `civiti.write` and keep read-only).
- **GDPR notice (mandatory):** "Data returned by tools flows to your AI assistant's provider (Anthropic, OpenAI, etc.) according to their terms. Civiti does not control this flow." Worded in Romanian.
- Remember-this-client checkbox.

### Copy ownership

Engineering owns the scaffold, the string keys, and the layout. **Final Romanian and English copy — in particular the GDPR notice — is owned by product/legal and must be reviewed before v1 ships.** This is not an engineering decision; it is a compliance artefact that belongs with whoever signs off on the privacy notice on civiti.app.

## 8. Scopes

| Scope | Grants |
| --- | --- |
| `civiti.read` | Read-only citizen tools. |
| `civiti.write` | Citizen write tools. |
| `civiti.admin.read` | Admin reads. Requires Supabase role `admin` + `UserProfile.McpAdminAccessEnabled`. |
| `civiti.admin.write` | Admin mutations. Same gate + two-step confirm (see §9). |

`Civiti.Mcp` enforces scopes on the Resource Server side by reading the validated token's scope claim — no additional lookups. Policy equivalents:

- `UserOnly` → requires `civiti.read` ∪ `civiti.write`.
- `AdminOnly` → requires admin scope *and* token role-claim still `admin` (stamped at issue, re-validated on refresh per §4).

> *Notation:* later sections use `civiti.admin.*` in prose as shorthand for "both `civiti.admin.read` and `civiti.admin.write`". **The string `civiti.admin.*` is never a literal scope value.** OpenIddict matches scopes by exact equality — allow-list rows, DCR requests, token claims, and enforcement checks always use the fully-qualified scope names.

## 9. Admin-specific hardening

See [`tool-inventory.md` §3](tool-inventory.md#3-admin-tools) for the two-step confirm mechanism.

1. **Opt-in per admin.** `UserProfile.McpAdminAccessEnabled` must be `true`. Default `false`. Set via the existing admin UI, never via MCP. **Flipping the flag from `true` to `false` is a kill-switch**: the write path in the admin UI immediately revokes every active MCP session carrying `civiti.admin.*` scopes for that user, so the stale-admin window on disable is zero (not the 5-minute sweep window). The background sweep in §4 is a belt-and-braces fallback — it also checks `McpAdminAccessEnabled`, not only the Supabase role — in case the kill-switch write path is ever bypassed (e.g. direct DB mutation during an incident).

   *Implementation mechanism:* the kill-switch lives on Civiti.Api's admin-UI write path. Civiti.Api registers OpenIddict's Core services (`AddOpenIddict().AddCore().UseEntityFrameworkCore()` against the shared `CivitiDbContext`) — this gives it `IOpenIddictTokenManager` and `IOpenIddictAuthorizationManager` without running the full Server stack. On flag flip, the handler enumerates tokens where `SubjectId == userId` and the authorization carries any `civiti.admin.*` scope, then calls `TryRevokeAsync` on each. No inter-service HTTP call is needed, because the OpenIddict stores are backed by the same Postgres instance all three hosts share. Civiti.Auth's RFC 7009 `/revoke` endpoint remains the canonical public revocation path for MCP clients but is not the mechanism used by this internal kill-switch.
2. **Stricter rate limits** on admin write tools.
3. **Two-step confirm for destructive actions.** Defense against prompt injection: an LLM context poisoned by user-generated content being moderated cannot mutate state on its own — the admin must explicitly invoke `confirm_admin_action(id)` in a fresh turn.
4. **Content quarantine in tool results.** User-submitted text returned to an admin agent is wrapped with clear delimiters and a "this content is untrusted" note.
5. **Audit alerts.** Every admin-scoped tool call emails the admin: "`approve_issue` on issue #1234 executed via Claude Desktop at 14:03. Revoke access at …".

## 10. Revocation

- **User-initiated:** Civiti web app → Settings → "Connected AI Assistants". Lists active sessions (client name, scopes, last-seen). Revoke → OpenIddict revocation endpoint → `RevokedAt` set; subsequent requests 401.
- **Admin-initiated:** Per-user kill-switch for incident response.
- **Automatic:**
  - On refresh, if the Supabase user is gone, disabled, or role changed.
  - On admin role loss for sessions carrying admin scopes (5-min background sweep).
  - **On `McpAdminAccessEnabled` being flipped to `false`** — immediate revocation of the user's admin-scoped sessions from the admin-UI write path; the 5-min sweep also checks this flag as a fallback.
  - On soft-delete of `UserProfile`.

## 11. Open questions

*None at present. Append new items here as they arise during review or implementation.*

## 12. Resolved decisions (log)

- **2026-04-21** — Split `Civiti.Auth` from `Civiti.Mcp` (AS vs RS per RFC 9728).
- **2026-04-21** — Do not persist Supabase refresh tokens. `Civiti.Auth` is the identity edge; we re-validate the Supabase user on Civiti-side refresh via the Admin API and forget.
- **2026-04-21** — Allow-list only for v1; Dynamic Client Registration endpoint is not exposed. *(Superseded later the same day — see DCR entry below.)*
- **2026-04-21** — OpenIddict is the AS and token-validation stack.
- **2026-04-21** — **Anonymous MCP surface in scope for v1 via a dedicated `/mcp/public` endpoint.** (Supersedes an earlier "out of scope" decision made the same day.) Rationale: the Civiti REST API already exposes an anonymous public surface (issue list/detail, authority directory, `email-sent` counter); the MCP path must mirror it for parity. Concrete use case: an AI agent helping a non-signed-up user browse issues, draft a petition email, and bump the email-sent counter — the petition itself is still citizen-originated from their own inbox. Sibling endpoint, IP-based rate limiting, no session or OAuth flow. Tool list defined in [`tool-inventory.md` §1](tool-inventory.md#1-public-anonymous-tools). Everything else in this document applies to the authenticated `/mcp` endpoint only.
- **2026-04-21** — **Token format: JWT access (15 min) + opaque rotating refresh (30 d).** JWT access → zero DB round-trip in `Civiti.Mcp` (validates via JWKS). Opaque refresh → server-side revocation is immediate. Industry default; one config switch in OpenIddict.
- **2026-04-21** — **Device Authorization Grant (RFC 8628) deferred.** All v1 allow-listed clients support browser redirect (loopback or claimed URL scheme). Add if and when a client that cannot open a browser is onboarded.
- **2026-04-21** — **Admin scopes (`civiti.admin.*`) restricted to `claude-desktop` and `claude-code` in v1.** Enforced via `allowsAdminScopes` flag on allow-list entries (§6). Other clients get the flag on a case-by-case review. Easy to expand; expensive to walk back once an admin has granted a wider client admin access.
- **2026-04-21** — **Consent-screen copy ownership: product/legal.** Engineering ships the scaffold; the GDPR notice wording is a compliance artefact, not an engineering decision. Blocking item for v1 launch.
- **2026-04-21** — **Supabase auth webhooks for push-based revocation deferred.** Refresh-time re-validation (~every access-token TTL for active users) plus the 5-minute background sweep for admin-scoped sessions (§4) bound the stale-state window acceptably for v1. Revisit if (a) stale-admin windows become a compliance concern, (b) Supabase offers webhooks with auth acceptable to our threat model, or (c) we observe refresh-time latency becoming user-visible.
- **2026-04-21** — **Dynamic Client Registration enabled with guardrails.** (Supersedes the earlier "DCR off" decision made the same day.) Rationale: every practical remote-MCP deployment uses DCR because clients expect it, and with the UX worked out end-to-end a Civiti user pasting `https://mcp.civiti.app/mcp` into Claude Desktop must not also have to paste a `client_id`. Safety shifts from "who can register" to "what redirect URIs are accepted and what scopes can be granted". Policy (§6): loopback-only redirect URIs (RFC 8252), per-IP rate limit on `/register`, DCR-registered clients capped at `civiti.read` + `civiti.write` (never admin), admin scopes stay gated on the pre-registered allow-list via `allowsAdminScopes`, consent screen displays a Verified/Unverified badge.
