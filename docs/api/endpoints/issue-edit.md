# Owner Edit & Re-Approval

How a citizen edits an issue they created, and what that does to the moderation loop.
The wire-level contract lives in [`api-specification.md`](../../api-specification.md#put-apiuserissuesid);
this page covers the flow and the decisions behind it.

> **Integrating a client?** Start with
> [`issue-edit-integration-notes.md`](../issue-edit-integration-notes.md) — it lists where the
> shipped behaviour diverges from the original requirements doc, including the changes that break
> a client written strictly to that spec.

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
| `Active` | yes | The live-issue case — see below |
| `Resolved`, `Cancelled` | no | Terminal |
| `Draft` | no | Unreachable: issue creation always produces `Submitted` |
| `Unspecified` | no | Invalid |

Anything outside the editable set returns `409` with code `ISSUE_NOT_EDITABLE`.

### Editing a live issue

An edited `Active` issue drops to `Submitted`, which **pulls it from public view until an admin
re-approves it**. Shared, QR-coded and indexed links return not-found in the meantime, and if the
reviewer rejects the edit the previously-approved public version is gone — there is no revision
history to roll back to. That is the accepted v1 trade-off; the owner can still read and re-edit
the issue throughout.

The issue keeps its supporter counters across the edit. That combination — preserved endorsements
plus fully replaced content — is only safe if the reviewer can see what actually changed, which
is what the [approved-content snapshot](#admin-re-review-diff) provides. **The diff is not
optional garnish: remove it and `Active` has to come out of the editable set with it.**

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

Ordered; index 0 becomes the primary photo. The position is **stored** on each row
(`IssuePhoto.DisplayOrder`) rather than inferred, and every read path orders by it through
`IssuePhotoOrdering.InDisplayOrder`.

Inferring it does not work: a photo set is written in one go, so every row shares a `CreatedAt`
and the id tiebreak is a fresh random GUID — photos would come back in an arbitrary sequence and
re-submitting an unchanged list would read as a change in the re-review diff. Rows predating the
column are all `0` and fall back to the previous ordering; since a photo set is always replaced
wholesale, a single issue never mixes the two.

**Known gap:** photos dropped from the list are unlinked from the issue but their blobs stay in
Supabase Storage, still reachable by URL. The backend has no storage client, so collecting them
needs a storage adapter and a sweep job. Tracked, not shipped.

## Admin re-review diff

`GET /api/admin/issues/{id}` returns, alongside the pending content:

- `approvedSnapshot` — the content as an admin last approved it, or `null`
- `changedFields` — which fields differ from it

```jsonc
{
  "title": "A completely different headline",   // the pending version
  // ...
  "approvedSnapshot": {
    "approvedAt": "2026-07-01T10:00:00Z",
    "title": "Groapă pe strada Mihai Eminescu",
    "photoUrls": ["https://.../a.jpg"],
    "authorities": [{ "name": "Primăria Sector 1", "email": "contact@ps1.ro" }]
    // ...the rest of the reviewable content
  },
  "changedFields": ["title", "location", "authorities"]
}
```

> **`approvedSnapshot: null` means "no approved baseline on record", not "nothing changed".**
> Both give an empty `changedFields`, so the client must branch on the snapshot's presence —
> rendering a never-approved issue as "unchanged" would be exactly backwards.
>
> Null covers **two** cases, and the API cannot distinguish them for you:
> 1. the issue has genuinely never been approved — a first review; and
> 2. it was approved before the snapshot table shipped and has not been edited or sent back
>    since, so no baseline was ever captured.
>
> Case 2 disappears the moment such an issue enters re-review (both the owner's edit and
> admin request-changes capture a baseline first), so anything actually **awaiting moderation**
> with a null snapshot is a true first review. A live `Active` issue viewed directly, on the
> other hand, may well be case 2 — so word the empty state around the issue's status rather than
> asserting "never approved".

Possible `changedFields` values: `title`, `description`, `category`, `address`, `district`,
`location`, `urgency`, `desiredOutcome`, `communityImpact`, `photos`, `authorities`.

### Comparison semantics

Computed server-side, because the rules are not obvious and a client should not have to
re-derive them:

- **`location`** covers latitude and longitude together — a coordinate is one thing to a
  reviewer. Compared exactly, with no tolerance: any tolerance wide enough to absorb float noise
  from a map widget is also wide enough to hide a deliberate small move. Over-reporting is cheap
  (the reviewer looks and sees the same address); under-reporting defeats the purpose.
- **Null and empty are the same value.** A legacy issue with a null `district` that the owner
  leaves blank has not changed anything.
- **Photos are ordered**, because index 0 is the primary photo — a reorder changes what the
  public sees.
- **Authorities are unordered.** Which institutions are targeted is meaningful; the sequence is
  not. Compared by name and (case-insensitive) email, so renaming a predefined authority or
  swapping in a different recipient both register.
  - Email case folding is **ordinal**, not invariant-lowercase. Invariant lowercasing applies
    full Unicode case mapping and collapses distinct code points onto ASCII — U+212A KELVIN SIGN
    becomes `k` — which would let a lookalike address compare equal to the approved one and pass
    re-review as "no changes". Ordinal folding still treats `CONTACT@` and `contact@` as the same
    mailbox but reports the lookalike.

### Where the baseline comes from

Three write paths, each in the same transaction as the change it describes:

1. **On approval** — `ApproveIssueAsync` records what it just approved, replacing any earlier
   snapshot. Bulk approve routes through the same method and is covered automatically.
2. **On editing a live issue** — if the issue is currently public and has no snapshot yet, its
   pre-edit content *is* the approved content, and that is captured before the replacement is
   applied. `approvedByUserId` is null for these: the approval predates the table and inventing
   an actor would be worse than admitting we do not know.
3. **On requesting changes to a live issue** — the same capture, for the same reason.
   `RequestChangesAsync` also takes a live issue out of public view, and it is the only other way
   that happens. Without it the baseline is lost permanently: the owner's follow-up edit sees a
   non-public status, skips path 2, and the re-review screen has nothing to compare against.

The invariant they exist to maintain: **an issue never leaves public view without a baseline on
record.** Any future path that moves an issue out of `IsPubliclyViewable` has to capture too.

Path 2 is why this shipped **without a data backfill**. The alternative was a migration that
rebuilt the same JSON in SQL across every live issue and executed against production the moment
it merged; the lazy capture reaches the same state, one issue at a time, at the only moment that
matters. The migration is a bare `CREATE TABLE`.

An edit never overwrites an existing snapshot — an edit is not an approval.

## Known gap: the admin announcement is best-effort

The re-announcement is enqueued on the admin-notify channel after the transaction commits. That
channel is bounded, and a full channel means the announcement is logged at `Error` and dropped —
the same delivery guarantee `POST /api/issues` has always had for a brand-new issue.

The consequence is bounded: `GET /api/admin/pending-issues` is the authoritative moderation
surface and always shows the resubmitted issue, so a dropped announcement costs latency, not
correctness. Capacity is tunable via `AdminNotify:ChannelCapacity`.

A process restart drops the same way, and the email leg has its own bounded channel — all three
failure modes, and the reconciliation sweep that would close them for the create path too, are
tracked in [#153](https://github.com/civiti/civiti-server/issues/153).

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
