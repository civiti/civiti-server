# Owner Edit & Re-Review — Client Integration Notes

**For:** the Civiti Angular client, integrating against the shipped backend.
**Written:** 2026-07-23, after PRs #152, #154 and #155 merged to `master` (live).

This is a **delta document**. The web client was built against
[`edit-issue-backend-requirements.md`](../../edit-issue-backend-requirements.md), and the
implementation diverged from it in ways that will break a client written strictly to that spec.
Everything below is what changed, not a full contract — for that see
[`api-specification.md`](../api-specification.md) (`PUT /api/user/issues/{id}`) and
[`endpoints/issue-edit.md`](endpoints/issue-edit.md) (flow and rationale).

---

## 1. Breaking: resolving is restricted to live issues

`PUT /api/user/issues/{id}/status` with `"status": "resolved"` now returns **`400`** unless the
issue is currently `Active`.

```jsonc
// 400
{ "error": "Only an issue that is currently live can be marked as resolved" }
```

**Action:** any "mark as resolved" affordance must be gated on `status === "active"`. If it is
offered on a `Rejected`, `Submitted` or `UnderReview` issue it will now fail.

**Why this appeared after the spec was written:** `Resolved` is a publicly-viewable status. The
previous rule allowed it from any non-terminal status, so a citizen could submit an issue and
immediately resolve it — publishing content no admin had ever seen — or edit an approved issue
and resolve it straight back into public view, skipping the re-review the edit exists to force.

`Cancelled` is unchanged: still allowed from any non-terminal status, and it is not publicly
viewable.

---

## 2. Breaking: the edit request is stricter than the spec

### Every editable field is required on every call

The body is a **full replacement**, and an omitted field is a validation failure rather than a
"leave it alone". Required on every call:

`title`, `description`, `category`, `address`, `district`, `latitude`, `longitude`,
`resubmit`, `expectedUpdatedAt`

Optional: `urgency` (defaults to `medium`), `desiredOutcome`, `communityImpact`, `photoUrls`,
`authorities`.

Missing fields come back as a standard `ValidationProblemDetails`:

```jsonc
// 400
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "latitude": ["The Latitude field is required."],
    "district": ["The District field is required."]
  }
}
```

Note this is a **different shape** from the service-level errors in §5 — a client needs to handle
both.

### `expectedUpdatedAt` is required, not "recommended"

The spec listed it as recommended. It is mandatory. Send the `updatedAt` you last read for the
issue; the `updatedAt` in a successful response is the token for the next edit.

### `resubmit` must be `true`

`false` is rejected with a `400`, not ignored. There is no status in which a silent owner edit is
legitimate, so the flag is an acknowledgement rather than a switch.

### `description` now has a 10-character minimum

Enforced on **create and edit**. An existing issue with a shorter description cannot be saved
until it is lengthened — surface the message rather than blocking the form silently.

### `district` is required

---

## 3. Spec said one thing, backend does another

| Requirements doc | Shipped | Notes |
|---|---|---|
| `title` max **150** | max **200** | Backend is looser; keep 150 as a client rule if you prefer |
| `authorities` min **1** | **no minimum**, max 5 | The MCP `create_issue` tool takes no authorities, so issues legitimately have none; a server minimum would lock their owners out of editing |
| `Draft` possibly editable | **not editable** | Nothing produces `Draft`; issue creation always yields `Submitted` |
| Maybe add `issueResubmitted` activity | **not added** | The public activity feed emits **nothing** for a resubmit. Do not wait for an event that will not arrive |

### Editable statuses

`Rejected`, `Submitted`, `UnderReview`, `Active`. Anything else → `409` / `ISSUE_NOT_EDITABLE`.

### Status after a successful edit

| From | To |
|---|---|
| `Rejected` | `Submitted` |
| `Active` | `Submitted` — **leaves public view until re-approved** |
| `Submitted` | `Submitted` (unchanged) |
| `UnderReview` | `UnderReview` (unchanged) |

Editing an `Active` issue removes it from the public list and from
`GET /api/issues/{id}` for everyone **except its owner**, until an admin re-approves it. Shared
and QR links 404 in the meantime. Supporter counters are preserved throughout.

---

## 4. Two things that quietly do not happen

**No email is ever sent to the linked authorities** — not on edit, not on approval, not ever.
Nothing in the backend mails them; the citizen sends the petition from their own mail client
(`POST /api/issues/{id}/petition-body` composes the text, `POST /api/issues/{id}/email-sent`
only increments a counter). **Any copy promising that authorities will be notified is wrong.**

**Photos removed from an issue are not deleted from storage.** The row is unlinked but the blob
stays in Supabase Storage and remains reachable by URL. If the uploader assumed backend cleanup,
it does not exist. Tracked as a known gap in [`endpoints/issue-edit.md`](endpoints/issue-edit.md).

---

## 5. Error handling for `PUT /api/user/issues/{id}`

| Status | Body | Meaning |
|---|---|---|
| `200` | full `IssueDetailResponse` | Navigate with this payload; no re-fetch needed |
| `400` | `ValidationProblemDetails` (`errors` map) | Field-level validation — surface per field |
| `400` | `{ "error": "..." }` | Moderation block, or a photo/authority rule. Show the message |
| `401` | — | Genuine auth failure; refresh → retry → sign out |
| `403` | empty | Authenticated but not the creator |
| `403` | `ProblemDetails`, title `Account Deleted` | Soft-deleted account |
| `404` | `{ "error": "..." }` | No such issue |
| `409` | `{ "error": "...", "code": "..." }` | See below |

### Always branch on `code`, never on the message

```jsonc
// 409 — the issue's status no longer permits editing
{ "error": "An issue with status 'Resolved' can no longer be edited.",
  "code": "ISSUE_NOT_EDITABLE" }

// 409 — someone changed the issue since you loaded it (typically an admin decision)
{ "error": "This issue changed since you opened it. Reload it and apply your edits again.",
  "code": "ISSUE_EDIT_CONFLICT" }
```

- `ISSUE_NOT_EDITABLE` → stop offering the edit action for this issue.
- `ISSUE_EDIT_CONFLICT` → reload the issue and let the owner reapply their changes. **Nothing was
  written**, so it is safe to retry after reloading.

The messages are prose and may be reworded or localised; the codes are stable.

---

## 6. New surface: the admin re-review screen

`GET /api/admin/issues/{id}` gained two fields:

```jsonc
{
  "title": "A completely different headline",   // the pending version
  // ...
  "approvedSnapshot": {
    "approvedAt": "2026-07-01T10:00:00Z",
    "title": "Groapă pe strada Mihai Eminescu",
    "description": "...",
    "category": "infrastructure",
    "address": "Strada Mihai Eminescu, Nr. 45",
    "district": "Sector 2",
    "latitude": 44.4268,
    "longitude": 26.1025,
    "urgency": "medium",
    "desiredOutcome": "...",
    "communityImpact": "...",
    "photoUrls": ["https://.../a.jpg"],
    "authorities": [{ "name": "Primăria Sector 1", "email": "contact@ps1.ro" }]
  },
  "changedFields": ["title", "location", "authorities"]
}
```

`changedFields` values: `title`, `description`, `category`, `address`, `district`, `location`
(latitude and longitude together), `urgency`, `desiredOutcome`, `communityImpact`, `photos`,
`authorities`.

### ⚠ The null trap

**`approvedSnapshot: null` does not mean "nothing changed".** An unchanged issue and a
never-approved one both return an empty `changedFields`, so **branch on the snapshot's presence,
not on the array being empty.**

Null covers two cases the API cannot tell apart for you:

1. the issue has genuinely never been approved — a first review; and
2. it was approved before this feature shipped and has not been edited or sent back since, so no
   baseline was ever captured.

Case 2 disappears the moment such an issue enters re-review (both an owner edit and admin
request-changes capture a baseline first), so anything **awaiting moderation** with a null
snapshot is a true first review. A live `Active` issue viewed directly may well be case 2 — word
the empty state around the issue's status rather than asserting "never approved".

### Pending queue: `isReReview`

`GET /api/admin/pending-issues` items gained `isReReview: boolean` — true when the item is an
edit of already-approved content rather than a new report.

**These deserve a visible badge.** They carry supporter counters earned under *different*
content, so approving one without opening the diff is how an endorsed issue gets swapped for
something else. Bulk approve never loads the detail screen, so the queue is the only place a
reviewer can be warned.

---

## 7. Smaller contract details worth checking

**Response enums are camelCase.** `"status": "submitted"`, `"category": "publicServices"`,
`"urgency": "medium"` — even though several examples in the requirements doc show PascalCase.
Requests accept either (parsing is case-insensitive). The `approvedSnapshot` examples in
`api-specification.md` were wrong on this until 2026-07-22.

**Photo order round-trips.** Send `photoUrls` ordered, index 0 becomes the primary photo, and the
response returns them in the same order. Previously non-primary photos came back in an arbitrary
sequence, so a client that re-sent what it received could reorder them by accident.

**The edit form's prefill works on non-public issues.** `GET /api/issues/{id}` returns an issue in
**any** status to its creator, and only `Active`/`Resolved` to anyone else. This is what lets an
owner open the form for a `Rejected` issue, or keep working on one their edit just pushed back
into the queue.

**The creator can never be changed** by the request body, and the response `user` is always the
original creator.

---

## Integration checklist

- [ ] "Mark as resolved" gated on `status === "active"` (§1)
- [ ] Edit form sends **all** required fields on every save (§2)
- [ ] `expectedUpdatedAt` sent, and refreshed from each success response (§2)
- [ ] `resubmit: true` always sent (§2)
- [ ] Client-side minimum of 10 characters on `description` (§2)
- [ ] Both `400` shapes handled — `errors` map and `{ error }` (§5)
- [ ] `409` handled by `code`, with a reload path for `ISSUE_EDIT_CONFLICT` (§5)
- [ ] Edit button shown for `Rejected`, `Submitted`, `UnderReview`, `Active` — and not `Draft` (§3)
- [ ] Editing an `Active` issue removes it from the public list slice until re-approval (§3)
- [ ] No copy claiming authorities are emailed (§4)
- [ ] Admin re-review branches on `approvedSnapshot !== null`, not on `changedFields.length` (§6)
- [ ] `isReReview` badge in the pending queue (§6)
- [ ] Enum comparisons use camelCase response values (§7)
