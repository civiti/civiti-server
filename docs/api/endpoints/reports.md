# Report Endpoints

## POST /api/issues/{id}/report

Report an issue for moderation.

**Auth:** Required
**Rate limit:** 5 reports per user per hour (across all targets)

### Request

```json
{
  "reason": "Spam",        // Required. One of: Spam, Harassment, Inappropriate, Misinformation, Other
  "details": "Optional details about the report (max 500 chars)"
}
```

### Responses

| Status | Description |
|--------|-------------|
| 201 | Report submitted successfully |
| 400 | Invalid reason, own content, or issue not in reportable state (`IssueNotReportable`) |
| 401 | Not authenticated |
| 403 | Account deleted |
| 404 | Issue not found |
| 409 | Already reported this issue |
| 429 | Rate limited (includes `Retry-After: 3600` header) |

### Auto-moderation

After 3 reports, the issue is automatically flagged (`IsFlagged = true`) for admin review. Flagged issues remain visible until an admin takes action.

---

## POST /api/comments/{id}/report

Report a comment for moderation.

**Auth:** Required
**Rate limit:** 5 reports per user per hour (across all targets)

### Request

Same as issue report.

### Responses

| Status | Description |
|--------|-------------|
| 201 | Report submitted successfully |
| 400 | Invalid reason, own content, or comment's parent issue not active (`CommentNotReportable`) |
| 401 | Not authenticated |
| 403 | Account deleted |
| 404 | Comment not found |
| 409 | Already reported this comment |
| 429 | Rate limited (includes `Retry-After: 3600` header) |

### Auto-moderation

After 3 reports, the comment is automatically hidden (`IsHidden = true`). Hidden comments return empty content in API responses.

---

## Implementation Details

- Reports are stored in a single `Reports` table with a `TargetType` discriminator (`Issue` or `Comment`)
- Duplicate detection uses a unique index on `(ReporterId, TargetType, TargetId)`
- Rate limiting uses a serializable transaction with an execution strategy wrapper for retry safety
- Counter increments use `ExecuteUpdateAsync` for atomicity under concurrent load
- `ReportStatus` field exists but transitions (Reviewed/Dismissed) are not yet implemented (TODO)
