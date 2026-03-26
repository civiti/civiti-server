# Mobile Integration Guide — Report, Block & Push Token Endpoints

## New Endpoints (PR #67)

| # | Method | Path | Auth | Request Body | Success | Error Codes |
|---|--------|------|------|-------------|---------|-------------|
| 1 | `POST` | `/api/issues/{id}/report` | Required | `{ "reason": string, "details": string? }` | `201 { id, message }` | 400, 401, 403, 404, 409, 429 |
| 2 | `POST` | `/api/comments/{id}/report` | Required | `{ "reason": string, "details": string? }` | `201 { id, message }` | 400, 401, 403, 404, 409, 429 |
| 3 | `POST` | `/api/user/blocked/{userId}` | Required | _(none)_ | `201 { blockedUserId, blockedAt }` | 400, 401, 403, 404, 409 |
| 4 | `DELETE` | `/api/user/blocked/{userId}` | Required | _(none)_ | `204` | 401, 403, 404 |
| 5 | `GET` | `/api/user/blocked` | Required | _(none)_ | `200 [{ userId, displayName, photoUrl, blockedAt }]` | 401, 403 |

### Report Request

```json
{
  "reason": "Spam",
  "details": "Optional details, max 500 chars"
}
```

**`reason` values** (case-insensitive, named strings only): `Spam`, `Harassment`, `Inappropriate`, `Misinformation`, `Other`

Numeric values (e.g. `"0"`, `"-1"`, `"+1"`) are rejected even if they map to a valid enum ordinal. Only the named strings above are accepted.

**`details`**: optional, max 500 characters.

### Report Response (201)

```json
{
  "id": "guid",
  "message": "Report submitted successfully"
}
```

### Block Response (201)

```json
{
  "blockedUserId": "guid",
  "blockedAt": "2026-03-20T12:00:00Z"
}
```

### Blocked Users List Response (200)

```json
[
  {
    "userId": "guid",
    "displayName": "Some User",
    "photoUrl": "https://...",
    "blockedAt": "2026-03-20T12:00:00Z"
  }
]
```

**Notes on blocked list:**
- Results are capped at **500 items**, ordered by most recently blocked first.
- Users who have deleted their accounts appear as `"displayName": "Deleted User"` with `"photoUrl": null`.

### Important Notes

- **`{id}` in report endpoints** is the target issue/comment `Guid`.
- **`{userId}` in block endpoints** is the internal `Guid` (the `id` field from user profile responses), **not** the Supabase string ID.
- Reporting your own content → `400`
- Reporting a non-active issue (draft, rejected, etc.) → `400` with `"Issue is not in a reportable state"`
- Reporting a comment on a non-active issue → `400` with `"Comment is not in a reportable state"`
- Duplicate report on same target → `409`
- More than 5 reports per hour → `429` (includes `Retry-After: 3600` header)

### Block Enforcement (Server-Side)

Blocking is enforced **server-side** in all content queries. When a user blocks another user:
- The blocked user's **issues are hidden** from the issue feed (`GET /api/issues`) and issue detail (`GET /api/issues/{id}`).
- The blocked user's **comments are hidden** from comment feeds and individual comment lookups.
- The mobile app does **not** need to filter blocked content client-side — the server handles it.

### Hidden Comments

Comments with 3+ reports are auto-hidden. When `isHidden` is `true`, the `content` field is returned as an **empty string** — the server redacts the content. The `isHidden` field is always present on `CommentResponse`:

```json
{
  "id": "guid",
  "content": "",
  "isHidden": true,
  ...
}
```

The mobile app should render a placeholder for hidden comments (e.g. "This comment has been hidden by moderators"). The original content is not available via the API when hidden.

### Error Response Format

All errors follow the existing pattern:

```json
{ "error": "Human-readable error message" }
```

| Code | Meaning |
|------|---------|
| 400 | Bad request (invalid reason, reporting own content, self-block, target not reportable) |
| 401 | Not authenticated |
| 403 | Account deleted |
| 404 | Target not found (issue, comment, or user) |
| 409 | Already reported / already blocked |
| 429 | Rate limited (5 reports/hour) — check `Retry-After` header for seconds until reset |

---

## Existing Endpoint (already deployed)

| # | Method | Path | Auth | Request Body | Success |
|---|--------|------|------|-------------|---------|
| 6 | `POST` | `/api/user/push-token` | Required | `{ "token": string, "platform": string }` | `200 { success: true }` |
| 7 | `POST` | `/api/user/push-token/deregister` | Required | `{ "token": string }` | `200 { success: true }` |

### Push Token Request

```json
{
  "token": "ExponentPushToken[xxxx]",
  "platform": "ios"
}
```

**`platform` values** (case-insensitive): `ios`, `android`, `web`

### Contract Note

The mobile spec expected `{ registered: true }` but the existing contract returns `{ success: true }`. The mobile app should adapt to this existing response shape.

### Push Token Deregister Request

```json
{
  "token": "ExponentPushToken[xxxx]"
}
```
