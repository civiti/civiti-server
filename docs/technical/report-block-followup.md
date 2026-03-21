# Report & Block — Follow-up Items

> Deferred from PR #67 (`feature/report-and-block-endpoints`).
> These items were identified by Greptile automated review and intentionally deferred as
> architectural or product-decision work beyond the scope of the initial endpoint PR.

---

## P1 — High Priority

### ~~1. Block-list enforcement absent from content queries~~ DONE

Implemented — block-list anti-join added to `GetIssueCommentsAsync` and `GetAllIssuesAsync`. Blocked users' content is hidden from feeds. Direct issue detail lookup by ID is intentionally not filtered (consistent with standard platform behavior).

---

### 2. Cascade-delete of reporter orphans ReportCount

**File:** `Civiti.Api/Data/Configurations/ReportConfiguration.cs:23-27`
**Impact:** Permanently flagged/hidden content with zero valid reports after reporter account deletion.

When a user hard-deletes their account, `OnDelete(DeleteBehavior.Cascade)` removes their `Report` rows, but `Issue.ReportCount`, `Issue.IsFlagged`, `Comment.ReportCount`, and `Comment.IsHidden` are never recalculated. An issue flagged by 3 reporters who all later delete their accounts will remain permanently flagged with no reports to justify it and no admin path to clear it.

**Fix approach (pick one):**
- **Option A:** Switch to `OnDelete(DeleteBehavior.Restrict)` or `SetNull` and handle report cleanup manually during account deletion.
- **Option B:** Add an EF Core `SaveChanges` interceptor that decrements the target entity's counter before cascade-deleting report rows.
- **Option C:** Add a background job / admin endpoint that recalculates `ReportCount`/`IsFlagged`/`IsHidden` from the actual `Reports` table.

Option C is the simplest and most resilient — it also provides a general "fix inconsistencies" tool for admins.

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

### 4. No DB-level CHECK constraint on TargetType

**File:** `Civiti.Api/Data/Configurations/ReportConfiguration.cs`
**Impact:** A direct SQL insert or future code path could write an invalid discriminator, silently breaking duplicate-report and rate-limit queries.

Application-level guards (`ReportTargetTypes` constants) are already in place, but the database has no enforcement.

**Fix approach:** Add a CHECK constraint and generate a migration:

```csharp
builder.ToTable(t => t.HasCheckConstraint(
    "CK_Reports_TargetType",
    $"\"TargetType\" IN ('{ReportTargetTypes.Issue}', '{ReportTargetTypes.Comment}')"));
```

---

### 5. Missing index on (ReporterId, CreatedAt) for rate-limit query

**File:** `Civiti.Api/Data/Configurations/ReportConfiguration.cs`
**Impact:** Rate-limit query scans all lifetime reports per user instead of just recent ones.

The rate-limit `CountAsync` filters by `ReporterId` and `CreatedAt > 1 hour ago`. The existing index covers `ReporterId` but not `CreatedAt`, forcing a heap fetch for every historical report row.

**Fix approach:** Add a composite index and generate a migration:

```csharp
builder.HasIndex(r => new { r.ReporterId, r.CreatedAt });
```

Not urgent at launch (report volume starts at zero), but should be added before significant user growth.

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
