# Block Endpoints

## POST /api/user/blocked/{userId}

Block a user. Blocked users' content is hidden from the authenticated user across all content queries (issue feeds, issue detail, comment feeds, comment detail).

**Auth:** Required

### Responses

| Status | Description |
|--------|-------------|
| 201 | User blocked successfully. Returns `{ blockedUserId, blockedAt }` |
| 400 | Cannot block self |
| 401 | Not authenticated |
| 403 | Account deleted |
| 404 | Target user not found |
| 409 | Already blocked |

---

## DELETE /api/user/blocked/{userId}

Unblock a user.

**Auth:** Required

### Responses

| Status | Description |
|--------|-------------|
| 204 | User unblocked successfully |
| 401 | Not authenticated |
| 403 | Account deleted |
| 404 | User not in block list |

---

## GET /api/user/blocked

Get the authenticated user's block list.

**Auth:** Required

### Responses

| Status | Description |
|--------|-------------|
| 200 | Returns array of `{ userId, displayName, photoUrl, blockedAt }` |
| 401 | Not authenticated |
| 403 | Account deleted |

### Notes

- Results are capped at 500 items
- Soft-deleted users appear as "Deleted User" with null photo
- List is ordered by `blockedAt` descending (most recently blocked first)

---

## Block Enforcement

Blocking is enforced at the query level across all content access paths:

- `GET /api/issues` — blocked users' issues excluded from feed
- `GET /api/issues/{id}` — returns 404 if author is blocked
- `GET /api/comments?issueId={id}` — blocked users' comments excluded
- `GET /api/comments/{id}` — returns null if author is blocked

Block relationships are stored in the `BlockedUsers` table with a unique index on `(UserId, BlockedUserId)` and a dedicated index on `BlockedUserId` for efficient cascade-delete operations.
