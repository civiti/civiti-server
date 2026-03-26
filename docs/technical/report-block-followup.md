# Report & Block — Follow-up Items

> Deferred from PR #67 (`feature/report-and-block-endpoints`).
> These items were identified by Greptile automated review and intentionally deferred as
> architectural or product-decision work beyond the scope of the initial endpoint PR.

---

## P1 — High Priority

### ~~1. Block-list enforcement absent from content queries~~ DONE

Implemented — block-list anti-join added to `GetIssueCommentsAsync` and `GetAllIssuesAsync`. Blocked users' content is hidden from feeds. Direct issue detail lookup by ID is intentionally not filtered (consistent with standard platform behavior).

---

### ~~2. Cascade-delete of reporter orphans ReportCount~~ DONE

Changed from `OnDelete(Cascade)` to `OnDelete(Restrict)` — hard deletion of a user profile now fails if they have reports, preventing silent orphaning of ReportCount/IsFlagged values. Reports must be explicitly cleaned up before user purge. A future admin recalculation endpoint (Option C from original proposal) is still recommended for resilience.

---

## P2 — Medium Priority

### 3. Hidden comment content blanked for the author

**File:** `Civiti.Api/Services/CommentService.cs:871`
**Impact:** Comment authors cannot see their own hidden content (no appeal/understanding path).

`MapToResponse` replaces content with `string.Empty` when `IsHidden == true` for all callers, including the comment author. This is a **product decision**: should authors be able to read their own moderated content?

**Fix approach (if yes):** Pass current user ID into `MapToResponse`:

```csharp
Content = (comment.IsHidden && comment.UserId != currentUserId)
    ? string.Empty
    : comment.Content,
```

**Decision needed:** Confirm moderation UX policy before implementing.

---

### ~~4. No DB-level CHECK constraint on TargetType~~ DONE

Added `CK_Reports_TargetType` CHECK constraint enforcing `TargetType IN ('Issue', 'Comment')` at the database level.

Application-level guards (`ReportTargetTypes` constants) are already in place, but the database has no enforcement.

**Fix approach:** Add a CHECK constraint and generate a migration:

```csharp
builder.ToTable(t => t.HasCheckConstraint(
    "CK_Reports_TargetType",
    $"\"TargetType\" IN ('{ReportTargetTypes.Issue}', '{ReportTargetTypes.Comment}')"));
```

---

### ~~5. Missing index on (ReporterId, CreatedAt) for rate-limit query~~ DONE

Added composite index `IX_Reports_ReporterId_CreatedAt` for efficient rate-limit queries.

---

### 6. Deleted users cannot be blocked

**File:** `Civiti.Api/Services/BlockService.cs:33-38`
**Impact:** Users cannot block a soft-deleted account (returns `TargetUserNotFound`).

The `AnyAsync` check applies the global query filter (`!u.IsDeleted`), so deleted accounts are invisible. This is likely intentional but creates asymmetry with the unblock flow.

**Decision needed:** Should blocking a deleted account be permitted (e.g., in case the account is restored)? If the current behavior is intentional, add a comment explaining why.

---

### 7. No rate limiting on block/unblock operations

**File:** `Civiti.Api/Services/BlockService.cs`
**Impact:** A client could rapidly alternate block/unblock in a tight loop, generating continuous INSERTs and DELETEs with no throttle.

Unlike report endpoints (capped at 5/hour), block/unblock has no rate limit. Consider applying a DB-based cap (e.g., 50 blocks per hour) or using `RequireRateLimiting` on the route group.

---

## Suggested Implementation Order

1. **Block-list enforcement** (#1) — Required for the feature to be user-visible
2. **Cascade-delete orphan fix** (#2) — Data integrity before account deletion is exercised
3. **Hidden content for author** (#3) — Product decision, then straightforward implementation
4. **CHECK constraint + rate-limit index** (#4, #5) — Bundle into a single migration PR
5. **Deleted users blocking** (#6) — Product decision, minimal code change
6. **Block rate limiting** (#7) — Low priority, add when abuse patterns emerge
