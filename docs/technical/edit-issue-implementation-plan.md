# Owner Edit of an Issue + Re-Approval — Backend Implementation Plan

> **Input:** [`edit-issue-backend-requirements.md`](../../edit-issue-backend-requirements.md) (repo root, 2026-07-22).
> **Status:** PR 1 implemented (2026-07-22). PR 2 (admin re-review diff) not started.
> **Scope:** `PUT /api/user/issues/{id}` full-field owner edit, re-approval loop, admin re-review diff.
>
> **Product decisions confirmed 2026-07-22:** `description` min 10 enforced on create and edit;
> `authorities` minimum **not** enforced server-side; `district` required. All three as
> recommended in §2.

---

## 1. Executive summary

The requirements document is broadly right about the *product*, but its picture of the
*current backend* is out of date in four places, and it misses one live security defect.
Net effect: the work is **less about adding fields** and **more about semantics, safety and
the missing admin diff** than the doc assumes.

**What's already true (no work needed):**

| Requirement | Reality |
|---|---|
| §3.1 "accepts only 4 fields today" | **Wrong.** `UpdateIssueRequest` already carries all 12 content fields (title, description, category, address, district, lat, lng, urgency, desiredOutcome, communityImpact, photoUrls, authorities) — see `Civiti.Application/Requests/Issues/UpdateIssueRequest.cs`. Only `resubmit` and `expectedUpdatedAt` are genuinely missing. |
| §5.2 step 3 "⚠ verify the queue filter" | **Verified, already correct.** `AdminService.GetPendingIssuesAsync` filters `Submitted ∪ UnderReview` (`AdminService.cs:29`), as do `Approve`/`Reject`/`RequestChanges` (`:228`, `:359`). Landing on `Submitted` reaches admins. No change. |
| §9.4 owner read of non-public issues | **Already implemented.** `IssueService.GetIssueByIdAsync` widens the status filter for the owner (`IssueService.cs:219-221`) and returns null to everyone else. Needs **tests + docs only** — it is currently an undocumented, untested invariant. |
| §7 "email re-blast landmine" | **Not applicable.** The backend never emails authorities. Citizens send the petition from their own mail client (`POST /api/issues/{id}/petition-body` composes the text; `POST /api/issues/{id}/email-sent` just increments a counter). Grep confirms zero senders read `IssueAuthority.CustomEmail`. Delete-and-recreate on replace is safe; **no email marker, no de-dup logic, no `EmailedAt` column.** This deletes the single largest piece of speculative work in the requirements. |

**What the requirements missed — a live moderation bypass (fix first, regardless of this feature):**

`CreateIssueAsync` runs OpenAI content moderation over title/description/address/district/
desiredOutcome/communityImpact (`IssueService.cs:320-334`) and rejects `javascript:`/`data:`/
`file:` photo URLs (`:341-361`). **`UpdateIssueAsync` does neither.** Today, anyone can post a
benign issue and then `PUT` abusive content or a `javascript:` photo URL into it — unmoderated,
unvalidated. This exists in production now and the edit feature widens it (more entry points,
more editable statuses). It is the highest-severity item in this plan.

**The real work, ranked:**

1. Moderation + photo-URL parity on the edit path (security, exists today).
2. Re-approval semantics: editable-status set, landing status `Submitted`, clear stale
   moderation artifacts, notify admins, write an audit trail.
3. Optimistic concurrency (`expectedUpdatedAt`).
4. Admin field-level diff — the confirmed gate on enabling `Active` edits (§5.3 / P3). This is
   the only piece needing a schema change.
5. Refactoring that makes 1–4 cheap and keeps them from drifting apart again (§5).

---

## 2. Answers to the open questions in §11

Everything below is answered from the code, not from preference, except where flagged **DECISION**.

| # | Question | Answer |
|---|---|---|
| 1 | Is `Draft` reachable? | **No.** `CreateIssueAsync` hard-codes `Status = IssueStatus.Submitted` (`IssueService.cs:399`); no endpoint produces `Draft`. **Exclude `Draft` from the editable set** — adding an unreachable branch is dead code. Editable set = `Rejected`, `Submitted`, `UnderReview`, `Active`. |
| 4 | Landing status | **`Submitted`.** The admin queue already covers `Submitted ∪ UnderReview`, so it works, and it correctly frees `UnderReview` to mean "an admin picked it up" (which `RequestChangesAsync` also sets, `AdminService.cs:489`). |
| 5 | `resubmit` always-on? | **Yes, and `resubmit=false` is rejected outright.** With `Draft` unreachable there is no status where a silent edit is legitimate, so `resubmit=false` has *no* valid branch. Keep the field in the contract (the frontend sends it) and 400 on `false` rather than silently ignoring it — an ignored flag is how moderation bypasses get shipped. |
| 6 | Counters | **Preserve.** `EmailsSent`/`CommunityVotes` are never touched by the edit path today; keep it that way. Explicit test. |
| 7 | Authorities after approval | **Freely replaceable, no de-dup needed** — see §1: nothing emails them. |
| 8 | Primary photo | **Keep positional (index 0 = primary)**, which is what create already does (`IssueService.cs:418`). A richer `[{url, isPrimary?, description?}]` input is not worth the contract churn while `IssuePhoto.Description` is never populated by any write path. **Add a deterministic sort to the response** (see §5.4) — today photo order is whatever Postgres returns, which quietly breaks "index 0 = primary" round-tripping and would make the admin photo diff flap. |
| 9 | Photo blob GC | **Defer, and say so in the acceptance criteria.** There is no Supabase Storage client in the backend at all (the frontend uploads directly; `SupabaseAdminClient` only wraps the Auth Admin API). Doing this properly means a new storage adapter + service-key handling + an orphan sweep. Note the real exposure: an unlinked blob stays publicly reachable by URL. Tracked as a follow-up, not a v1 blocker. |
| 10 | Concurrency token | **`expectedUpdatedAt` in the body, required.** Not `xmin`: the test suite runs on SQLite in-memory (`Civiti.Tests/Helpers/TestDbContextFactory.cs`), which has no `xmin`, so `UseXminAsConcurrencyToken` would make the edit path untestable. See §4.3 for a precision footgun that will otherwise cause spurious 409s. |
| 11 | Audit/activity | **Ship now, it's cheap.** `AdminAction` already has nullable actor fields, a `PreviousStatus`/`NewStatus` pair and is already surfaced on the admin detail screen (`AdminIssueDetailResponse.AdminActions`) — exactly where an admin asks "why is this back in my queue?". Append `AdminActionType.Resubmit`; enums are stored as ints so appending is additive with no migration. |
| 12 | Revision history | **Not in v1**, but §4.4's snapshot table is deliberately shaped so it becomes revision history by relaxing one unique index. |
| §6 | `title` max 150 vs 200 | **Keep 200.** Lowering the backend max to 150 would make every existing 150–200 char issue *unsavable by its owner* — a worse bug than the one it fixes. The frontend's 150 is a stricter client rule; a looser server is fine. |
| §6 | `description` min 10 | **DECISION.** Create has no minimum today, so sub-10-char descriptions can exist in production; enforcing min 10 on edit makes them unsavable until lengthened. Recommendation: enforce min 10 on **both** create and edit — the error is actionable and shown on a form the user is already filling in. Flagging because it is a (small) behaviour change to create. |
| §6 | `authorities` min 1 | **DECISION — recommend NOT enforcing min 1 server-side.** The MCP `create_issue` tool takes no authorities parameter at all (`Civiti.Mcp/Tools/MyIssueWriteTools.cs:17-30`), so MCP-created issues legitimately have zero. A hard min-1 on edit would permanently lock their owners out of editing. Recommendation: enforce `max 5` on both paths, treat min-1 as a frontend rule. If product wants min-1 server-side, it must land on create *and* MCP first, plus a backfill story for existing issues. |
| §6 | legacy null `district` | **DECISION — make `district` required, matching create.** PUT is a full replacement and the frontend always renders a district picker, so "untouched null" is not actually reachable through the real client. Alternative (accept null → keep stored value) reintroduces exactly the partial-update ambiguity §3.2 warns about. |

---

## 3. Current-state map (what the code does today)

`PUT /api/user/issues/{id}` → `UserEndpoints.cs:473-510` → `IssueService.UpdateIssueAsync`
(`IssueService.cs:996-1274`).

Today it:

- ✅ loads the issue, checks ownership (`:1031`), blocks `Cancelled`/`Resolved` (`:1037`)
- ✅ applies all 12 content fields (partial — `if (x != null)`)
- ✅ replaces photos and authorities wholesale
- ❌ **no content moderation, no photo-URL scheme check** (create has both)
- ❌ sets `Status = UnderReview` unconditionally (`:1193`) — including for a `Submitted` issue,
  and including for `Draft`/`Unspecified`
- ❌ never clears `RejectionReason` / `ReviewedAt` / `ReviewedBy` / `AdminNotes`, so a resubmitted
  issue arrives in the admin queue still wearing its old rejection reason
- ❌ never calls `adminNotifier.NotifyNewIssueAsync` — resubmits reach the queue **silently**
  (create announces itself, `IssueService.cs:534`)
- ❌ no audit entry, no activity entry
- ❌ no concurrency guard: a stale form clobbers a concurrent admin approve/reject
- ❌ returns `400 "No changes provided"` (`:1189`) — and the check is wrong anyway: supplying a
  photo or authority array counts as a change even when identical
- ⚠️ already silently pulls an `Active` issue from public view (`Active → UnderReview`), i.e.
  "pull-from-public" is *existing undocumented behaviour*, not a new risk introduced here

Also relevant: `PetitionBodyContentHash` (`Issue.cs`) fingerprints the prompt-affecting fields, so
the AI petition cache self-invalidates on edit. **Nothing to do** — worth a regression test.

---

## 4. Design decisions

### 4.1 Request contract — parity by construction

Rather than keeping two DTOs in sync by hand (they have already drifted), extract the shared
content fields into a base class:

```
Civiti.Application/Requests/Issues/
  IssueContentRequest.cs   (NEW — abstract; the 12 editable fields + all attributes)
  CreateIssueRequest.cs    (: IssueContentRequest — adds nothing)
  UpdateIssueRequest.cs    (: IssueContentRequest — adds Resubmit, ExpectedUpdatedAt)
```

"Edit must never reject a value create accepted, or vice-versa" (§6) then holds *structurally*
instead of by review discipline. `IssueAuthorityInput` stays where it is.

All numeric limits move into `Civiti.Domain/Constants/IssueValidationLimits.cs` (which today holds
only `MaxPhotoCount`) so the attributes, the MCP tool descriptions and the OpenAPI examples cite
one source.

Required-ness is expressed with nullable CLR types + `[Required]` (`double? Latitude` +
`[Required]`, not `double`), so a missing field yields a clean field-named 400 rather than a
silent `0.0` — the §3.2 "silent-null footgun" answered concretely. `Urgency` stays non-nullable
with `= UrgencyLevel.Medium`, which gives the §6 default for free.

Cross-field rules (`authorityId` XOR custom pair; `resubmit` must be `true`) go in
`IValidatableObject.Validate` on the DTOs. `builder.Services.AddValidation()` is already wired
(`Program.cs:548`), so these surface automatically as RFC 9457 `HttpValidationProblemDetails`
with per-field keys — which the frontend can consume directly.

> **⚠ Frontend contract note.** §6 asks for `400 ErrorResponse { error, details?, requestId? }`.
> The API does **not** have one error shape: built-in validation emits `ValidationProblemDetails`
> (`errors` dictionary), endpoints emit `{ error: "..." }` anonymous objects, and the exception
> middleware emits a *third* shape (`Civiti.Api/Infrastructure/Middleware/ErrorHandlingMiddleware.cs`
> declares its own `ErrorResponse`, unrelated to `Civiti.Application.Responses.Common.ErrorResponse`).
> **Do not unify these here** — it would touch every endpoint and break existing clients for no
> gain to this feature. Instead: document both shapes precisely for the frontend, and see §4.2 for
> the one place where machine-readable codes genuinely matter.

### 4.2 Status machine + distinguishable 409s

```
editable  = { Rejected, Submitted, UnderReview, Active }     // Draft excluded — unreachable
resubmit  : Rejected → Submitted
            Active   → Submitted        // pulls from public until re-approved
            Submitted/UnderReview → unchanged (content updated in place)
otherwise → 409
```

The state machine lives in one place — a small `IssueEditPolicy` static in `Civiti.Domain` — so
the endpoint, the service and the tests read the same table, and a future MCP `update_issue` tool
inherits it.

**Both 409 cases must be machine-distinguishable.** §9 puts "non-editable status" and
"concurrency conflict" on the same status code, but the client's response differs completely
(*"you can't edit this"* vs *"reload, it changed under you"*). String-matching English prose is
not an acceptable discriminator. Return a stable code alongside the message:

| Case | HTTP | `code` |
|---|---|---|
| Terminal/ineligible status | 409 | `ISSUE_NOT_EDITABLE` |
| `expectedUpdatedAt` mismatch | 409 | `ISSUE_EDIT_CONFLICT` |

On resubmit, clear `RejectionReason`, `ReviewedAt`, `ReviewedBy`, `AdminNotes` (§5.2 step 4) —
`AdminNotes` in particular, since `RequestChangesAsync` stores the requested changes there and a
stale copy would mislead the next reviewer.

Drop the `"No changes provided"` 400: with mandatory re-review, an identical resubmit is a
legitimate idempotent request, and the current detection is unreliable anyway.

### 4.3 Optimistic concurrency — and the precision trap

Compare `request.ExpectedUpdatedAt` against the stored `Issue.UpdatedAt` inside the same
transaction that performs the write; mismatch → 409 `ISSUE_EDIT_CONFLICT`, no mutation.

**The trap:** `UpdateIssueAsync` builds its response from the in-memory entity, so `updatedAt`
goes out to the client with .NET's 100 ns tick precision, while Postgres `timestamp` stores
microseconds. The client echoes back the value it was given, it fails to equal the truncated
stored value, and **every second consecutive edit 409s**. Fix once, centrally: a
`Civiti.Domain` time helper that truncates to microseconds, used wherever `UpdatedAt`/`CreatedAt`
are stamped on `Issue`. Cheaper and more honest than comparing with a tolerance window.

Make `expectedUpdatedAt` **required**. It is safe to do so: `PUT /api/user/issues/{id}` is
documented in neither `docs/api-specification.md` nor `MOBILE-INTEGRATION-GUIDE.md`, and the
only consumer is the Angular client currently being built against this contract. An optional
token silently degrades to last-writer-wins for any client that forgets it.

### 4.4 Admin diff (§5.3 / decision P3) — last-approved snapshot

Confirmed as a v1 blocker on enabling `Active` edits. Design:

```
IssueApprovedSnapshot            (NEW entity + table)
  IssueId      PK/FK, unique     ← one row per issue in v1
  ApprovedAt   timestamptz
  ApprovedBy   uuid?             ← admin UserProfile id
  Payload      jsonb             ← approved title/description/category/address/district/
                                   lat/lng/urgency/desiredOutcome/communityImpact
                                   + ordered photo urls + ordered authority {name,email}
```

- **Written** in `ApproveIssueAsync`, inside the existing transaction (`AdminService.cs:249-281`),
  as an upsert. Bulk approve routes through the same method (`AdminService.cs:706`), so it is
  covered automatically.
- **Read** by `GET /api/admin/issues/{id}`: `AdminIssueDetailResponse` gains
  `approvedSnapshot: {...}?` and `changedFields: string[]` (computed server-side, so the admin UI
  renders a diff without reimplementing comparison semantics — normalisation, photo ordering and
  authority identity are backend concerns).
- **No baseline case:** a never-approved issue returns `approvedSnapshot: null` and
  `changedFields: []`. The admin UI must render "first review" rather than "nothing changed" —
  a contract point to state explicitly for the frontend.
- **Backfill** in the migration for issues currently `Active` or `Resolved` only: their present
  content *is* the approved content. Deliberately **not** `Submitted`/`UnderReview` — some of
  those may already hold post-edit content under today's behaviour, and snapshotting that would
  fabricate a baseline that was never approved.
- **Forward path:** relax the unique index from `IssueId` to `(IssueId, Version)` and this becomes
  the revision history of §11 Q12 without a rewrite.

Why a side table rather than JSON columns on `Issue`: `Issue` is already wide and on the hot read
path for every list query; the snapshot is written rarely and read only on the admin detail screen.

### 4.5 Notifications on resubmit

Call `adminNotifier.NotifyNewIssueAsync(issue.Id)` after commit, best-effort inside try/catch,
mirroring create (`IssueService.cs:531-539`). Without it, resubmits land in the queue with no
signal to anyone — the "moderation smell" §8 warns about.

Do **not** email the owner a "submitted" confirmation on resubmit (they just clicked save);
do **not** touch authority email or counters (§5.3).

---

## 5. Refactoring carried out as part of this work

Not gold-plating — each item is duplication that has *already* produced a live divergence, and
each one is on the path of the changes above.

1. **`Issue → IssueDetailResponse` mapping** is duplicated ~50 lines in `GetIssueByIdAsync`
   (`:248-295`) and `UpdateIssueAsync` (`:1211-1258`), and has already drifted (the update copy
   hard-codes `HasVoted = null`). Every new response field has to be added twice. → Extract
   `IssueResponseMapper` (Application layer, pure function over the loaded entity).
2. **Authority validation + materialisation** is duplicated ~70 lines between `CreateIssueAsync`
   (`:426-497`) and `UpdateIssueAsync` (`:1102-1180`), with the same rules expressed as thrown
   `InvalidOperationException` in one and returned tuples in the other. → Extract
   `IssueAuthorityWriter`, used by both.
3. **Photo materialisation + URL guard** — same shape (`:407-423` / `:1077-1099`), guard present
   only in create. → Extract `IssuePhotoWriter`; the guard then applies to both by construction,
   which is the fix for the §1 defect rather than a copy-paste of it.
4. **Deterministic photo ordering** — neither read path orders `Photos`. Order by
   (`IsPrimary` desc, `CreatedAt`, `Id`) in the mapper so "index 0 = primary" round-trips and the
   admin photo diff doesn't flap on row order.
5. **`IssueService.cs` is 1575 lines.** These extractions take roughly 250 lines out of it. Not
   proposing a full split in this batch — that is `docs/technical/refactor-extraction-plan.md`
   territory and would bury the feature diff.

Explicitly **not** doing here: unifying the three error-response shapes (§4.1), splitting
`IssueService`, adding a storage adapter (§2 Q9), MCP `update_issue`.

---

## 6. Work breakdown

Two PRs. Per the repo convention this is deliberately *few and large* — one cohesive theme each —
rather than a PR per bullet.

### PR 1 — "Owner edit: parity, safety and re-approval semantics" ✅ implemented

Everything except the diff. **Ships with `Active` NOT yet in the editable set**, so the §5.3
constraint ("editing `Active` issues is not enabled in production until the diff is in place")
is honoured by sequencing rather than by a feature flag. `Rejected`/`Submitted`/`UnderReview`
edits — which need no diff — become correct immediately.

| Area | Files |
|---|---|
| Shared limits | `Civiti.Domain/Constants/IssueValidationLimits.cs` |
| Edit policy | `Civiti.Domain/…/IssueEditPolicy.cs` (new), micros-truncating time helper |
| DTOs | `IssueContentRequest.cs` (new), `CreateIssueRequest.cs`, `UpdateIssueRequest.cs` (+`Resubmit`, `ExpectedUpdatedAt`, `IValidatableObject`) |
| Extractions | `IssueResponseMapper`, `IssueAuthorityWriter`, `IssuePhotoWriter` |
| Service | `IssueService.CreateIssueAsync` / `UpdateIssueAsync`: moderation + photo-URL guard on edit, status machine, clear moderation artifacts, concurrency check, admin notify, audit entry, drop "no changes" 400 |
| Audit | `AdminActionType.Resubmit` (appended); optional `ActivityType.IssueResubmitted` |
| Contract | `IIssueService.UpdateIssueAsync` returns a typed outcome instead of `(bool, T?, string?)` so the endpoint can map 403/404/409-not-editable/409-conflict without string matching |
| Endpoint | `UserEndpoints.cs:473-510` — new status mapping, `code` on 409s, updated OpenAPI summary/description/`Produces` |
| Tests | see §7 |
| Docs | see §8 |

### PR 2 — "Admin re-review diff + enable Active edits"

| Area | Files |
|---|---|
| Entity + config | `IssueApprovedSnapshot.cs`, `IssueApprovedSnapshotConfiguration.cs`, `CivitiDbContext` |
| Migration | table + unique index + backfill for `Active`/`Resolved` |
| Write | `AdminService.ApproveIssueAsync` — upsert snapshot inside the existing transaction |
| Read | `AdminIssueDetailResponse` gains `approvedSnapshot`/`changedFields`; diff computation service |
| Flip | add `Active` to `IssueEditPolicy` editable set |
| Tests | snapshot written on approve + bulk approve; changed-field computation per field type; no-baseline case; `Active` → `Submitted` pulls from public |

> **Deployment note.** Per `reference_railway_branch_topology`, migrations only reach production
> via a `master → production` merge. PR 2 carries a schema change with a data backfill — it needs
> its own promotion, and the frontend must not enable the "edit a live issue" entry point until
> that promotion lands.

---

## 7. Test plan

Existing coverage for `UpdateIssueAsync` is **zero** (`Civiti.Tests/Services/IssueServiceTests.cs`
has no update tests; `UpdateIssueRequestValidatorTests` only exercises the photo-count attribute).
Tests use SQLite in-memory via `TestDbContextFactory`, with `TestDataBuilder` for fixtures.

**Authorization / concurrency**
- non-owner → `EditOwnIssuesOnly`, and assert **nothing was written**
- deleted account → 403 path
- `expectedUpdatedAt` mismatch → conflict, no mutation
- **round-trip test:** edit → take `updatedAt` from the response → edit again with it → must
  succeed (this is the §4.3 precision trap; without this test it ships broken)
- body cannot change `userId`/`id` — response `user` is always the original creator

**State machine**
- `Rejected` → `Submitted`; `Active` → `Submitted`; `Submitted` → `Submitted`;
  `UnderReview` → `UnderReview`
- `Resolved`/`Cancelled`/`Unspecified` → not-editable conflict
- `resubmit: false` → 400
- resubmitted issue appears in `GetPendingIssuesAsync`
- edited `Active` issue disappears from `GetAllIssuesAsync` (public list)
- `RejectionReason`/`ReviewedAt`/`ReviewedBy`/`AdminNotes` cleared

**Safety (regression tests for the §1 defect)**
- flagged content on edit → `ContentModerationException`, issue unchanged
- `javascript:` / `data:` photo URL on edit → rejected
- over-length photo URL → rejected

**Data preservation**
- `EmailsSent`/`CommunityVotes` unchanged across an edit
- photos/authorities fully replaced; ordering deterministic; `IsPrimary` on index 0
- `PetitionBodyContentHash` invalidated when a prompt-affecting field changes

**Read contract (§9.4 — currently untested)**
- owner reads own `Submitted`/`Rejected` issue via `GetIssueByIdAsync` → returns it
- stranger reads the same → null
- blocked-user filter still applies

**Admin loop (PR 2)**
- snapshot written on approve and on bulk approve
- `changedFields` correct for scalar, photo-list and authority-list changes
- never-approved issue → `approvedSnapshot: null`, `changedFields: []`
- resubmit → approve → back to `Active`/public

**Build gate.** Per `feedback_test_project_version_pinning`, run the full local CI mirror
(`dotnet build` warnings-as-errors + `dotnet test`) **and** a Docker build before pushing —
Actions CI does not catch the `Civiti.Tests` ↔ Dockerfile skew.

---

## 8. Documentation updates (required — project doc protocol)

- `docs/api-specification.md` — add `PUT /api/user/issues/{id}` (currently undocumented): full
  request/response, editable-status table, both 409 codes, `expectedUpdatedAt` semantics.
- `docs/api/endpoints/issues.md` (new) — owner edit + re-approval flow end to end, including the
  §9.4 owner-read invariant that is currently undocumented.
- `docs/technical/admin-re-review-diff.md` (new, PR 2) — snapshot model, `changedFields` semantics,
  the no-baseline contract, backfill rationale, and the revision-history upgrade path.
- `docs/database-schema.md` — `IssueApprovedSnapshot`.
- This plan — mark decisions as they are confirmed.

---

## 9. Risks and accepted gaps

| Risk | Disposition |
|---|---|
| Editing `Active` pulls a live issue from public; shared/QR links 404 during re-review; an admin rejection loses the previously-approved public version (no rollback) | **Accepted for v1** (confirmed decision). Mitigated by the diff. Note this is already today's behaviour, just now deliberate and documented. |
| Unlinked photo blobs stay publicly reachable in Supabase Storage | **Deferred** — no storage adapter exists (§2 Q9). Tracked as a follow-up; must be in the acceptance criteria as a known gap rather than silently unchecked. |
| Enforcing `description` min 10 blocks editing legacy short descriptions | Accepted; actionable error on a form the user is already completing. **DECISION** — confirm with product. |
| Enforcing `authorities` min 1 would lock owners out of MCP-created issues | **Not enforcing.** **DECISION** — confirm with product. |
| Three error-response shapes on the wire | Pre-existing; explicitly out of scope. Mitigated for this endpoint by stable `code` values on the 409s. |
| Moderation adds a 300 ms–2 s OpenAI round-trip to edits | Follow create's pattern: moderate **before** `BeginTransactionAsync` so no pooled DB connection is held across the call (`IssueService.cs:306-310`). |
