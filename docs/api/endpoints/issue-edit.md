# Owner Edit & Re-Approval

How a citizen edits an issue they created, and what that does to the moderation loop.
The wire-level contract lives in [`api-specification.md`](../../api-specification.md#put-apiuserissuesid);
this page covers the flow and the decisions behind it.

## The loop

```
                 ┌──────────────────────── owner edits ────────────────────────┐
                 │                                                             │
  create ──▶ Submitted ──▶ (admin picks up) UnderReview ──▶ approve ──▶ Active │
                 │                    │                                        │
                 │                    └── reject ──▶ Rejected ─────────────────┘
                 └── owner edits ──▶ stays Submitted
```

1. `PUT /api/user/issues/{id}` replaces the issue's editable content.
2. The issue lands back in a pending status and reappears in
   `GET /api/admin/pending-issues` (which selects `Submitted` ∪ `UnderReview`).
3. The previous review's verdict is wiped — `rejectionReason`, `reviewedAt`, `reviewedBy`,
   `adminNotes` — because it describes text that no longer exists.
4. An `AdminAction` of type `Resubmit` is written, carrying the owner and the
   `previousStatus → newStatus` pair. It shows up in the moderation history on the admin detail
   screen, which is where a reviewer asks "why is this back in my queue?".
5. The admins are re-announced to via the normal new-issue fanout.
6. An admin approves or rejects it as usual. No new admin endpoints are involved.

## Editable statuses

| Status | Editable | Why |
|---|---|---|
| `Rejected` | yes | The original flow — fix what the reviewer objected to |
| `Submitted` | yes | Fix a mistake spotted right after submitting |
| `UnderReview` | yes | Content is updated in place; the status is left alone |
| `Active` | **not yet** | Pending the admin re-review diff — see below |
| `Resolved`, `Cancelled` | no | Terminal |
| `Draft` | no | Unreachable: issue creation always produces `Submitted` |
| `Unspecified` | no | Invalid |

Anything outside the editable set returns `409` with code `ISSUE_NOT_EDITABLE`.

### Why `Active` is still closed

Editing a live issue is the point of this feature, but it is only safe alongside a field-level
diff on the admin re-review screen. An edited `Active` issue keeps its supporter counters
(`emailsSent`, `communityVotes`) and drops back to `Submitted`, which pulls it from public view
until re-approved. Without a diff, a reviewer re-approving it sees a wall of text with no
indication of what changed — so an issue approved on benign content could be quietly swapped for
spam and keep every endorsement it had earned.

The diff (a last-approved snapshot captured at approve time, plus a computed `changedFields`)
ships separately; `Active` joins `IssueEditPolicy.EditableStatuses` in the same change.

## Optimistic concurrency

The client sends the `updatedAt` it last read as `expectedUpdatedAt`. A mismatch is rejected with
`409` / `ISSUE_EDIT_CONFLICT` and **nothing is written**.

This is not defensive padding. The realistic collision is an owner sitting on an open edit form
while an admin approves or rejects the issue: without the check the owner's stale form wins and
silently reverts a moderation decision.

Both sides of the comparison are truncated to microseconds — the resolution PostgreSQL actually
stores — via `UtcTimestamp`. .NET's `DateTime` carries finer ticks, so an untruncated timestamp
handed back in a response can never equal the value later read from the database, and every
second consecutive edit would fail with a bogus conflict. `IssueServiceUpdateTests` covers the
round-trip explicitly.

Comparing a value that was *read* is not on its own enough: two owner requests can both read the
same `updatedAt`, both find it current, and both proceed — at which point the second write
silently clobbers the first, the exact lost update the token exists to prevent. So the edit also
**claims the row with a conditional `UPDATE`** (`WHERE id = @id AND updatedAt = @loaded`) before
mutating anything. The loser blocks on the winner's row lock, re-evaluates its predicate against
the committed value, matches zero rows, and gets `ISSUE_EDIT_CONFLICT`.

That guard is scoped to this one operation rather than marking `Issue.UpdatedAt` as an EF
concurrency token, which would turn every other writer of an issue — approve, reject,
request-changes, votes, email counters — into a source of `DbUpdateConcurrencyException` that
none of them handle today.

## Order of operations

1. **Authorize** — load the caller and the issue, check existence, ownership, editable status and
   the concurrency token.
2. **Moderate** — the external content check, plus the photo-URL guard.
3. **Apply** — open the transaction, re-run every check from step 1, claim the row, write.

Authorization comes first because moderation is an external, billed call: reaching it before the
ownership check would let any authenticated user spend the moderation budget against issues they
do not own, and probe for moderation-specific responses on issues they cannot see.

Step 1 is nevertheless only advisory. It is a snapshot taken before a round-trip that can take
seconds, so an admin can act in that window — which is why step 3 repeats it, and why that repeat
is the binding one.

None of this holds a pooled database connection across the moderation call: step 1's queries
complete and release their connection before step 2 begins.

## Full replacement, not a patch

Every editable field must be present on every call, and `photoUrls` / `authorities` are the
complete desired sets. An all-optional partial body would let a client that simply forgets a
field silently blank a stored value.

Required fields are declared as nullable CLR types with `[Required]`, so an omitted `latitude`
fails validation by name instead of binding to `0.0` — the Gulf of Guinea.

### Photos

Ordered; index 0 becomes the primary photo. Responses order photos deterministically (primary
first, then oldest first, then by id) so that convention round-trips.

**Known gap:** photos dropped from the list are unlinked from the issue but their blobs stay in
Supabase Storage, still reachable by URL. The backend has no storage client, so collecting them
needs a storage adapter and a sweep job. Tracked, not shipped.

## Known gap: the admin announcement is best-effort

The re-announcement is enqueued on the admin-notify channel after the transaction commits. That
channel is bounded, and a full channel means the announcement is logged at `Error` and dropped —
the same delivery guarantee `POST /api/issues` has always had for a brand-new issue.

The consequence is bounded: `GET /api/admin/pending-issues` is the authoritative moderation
surface and always shows the resubmitted issue, so a dropped announcement costs latency, not
correctness. Capacity is tunable via `AdminNotify:ChannelCapacity`. A durable outbox is the real
fix and should cover the create path at the same time; it is not part of this work.

### Authorities

Replaced wholesale — links no longer in the list are deleted, new ones inserted. This is safe
because **nothing in the system emails these rows**: the petition goes out from the citizen's own
mail client (`POST /api/issues/{id}/petition-body` composes the text), and
`POST /api/issues/{id}/email-sent` only increments a counter. So replacing a link cannot
re-notify anyone who was already contacted, and no email marker is needed to prevent it.

No minimum count is enforced. The MCP `create_issue` tool takes no authorities argument, so
issues legitimately exist with none; a server-side minimum would lock their owners out of editing
forever. Requiring at least one is a client-side rule.

## Validation parity with create

`CreateIssueRequest` and `UpdateIssueRequest` both inherit `IssueContentRequest`, so they share
every field and every validation attribute. The rule "an edit must never reject a value create
accepted, or vice-versa" is structural rather than a review-time promise, and
`IssueContentRequestParityTests` fails loudly if the two ever diverge.

Photo-URL and authority rules live in the shared writers (`IssuePhotoWriter`,
`IssueAuthorityWriter`) rather than on the DTOs, because the MCP tools call the services directly
and never pass through HTTP model validation — a DTO-only rule would simply not run for them.

## Content moderation

Edits go through the same OpenAI moderation gate as creation. This is load-bearing: moderating
only on create is no gate at all, since an author could publish something benign and then edit
the abusive content in, reaching exactly the same read surfaces. Moderation runs before any
transaction is opened, so the provider round-trip never holds a pooled database connection.

## Reading a non-public issue

The edit form prefills from `GET /api/issues/{id}`, which returns an issue **in any status to its
creator**, and only `Active` / `Resolved` issues to anyone else. That owner clause is what lets
someone open the form for a `Rejected` issue, or keep working on one an edit just pushed back
into the queue. It must stay exactly this narrow: a stranger holding a known id must not be able
to read the content of an issue that was never approved.

## What an edit never does

- reset `emailsSent` or `communityVotes` — they record what the community did, not what the text
  says, and clearing them would punish an owner for fixing a typo
- change the creator or the issue id, whatever the body claims
- send or retract email to the linked authorities
- accept a client-supplied `status`
