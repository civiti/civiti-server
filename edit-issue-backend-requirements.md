# Backend Requirements — Owner Edit of an Issue (All Fields) + Re-Approval

**Endpoint owner:** .NET backend (Railway)
**Consumer:** Civiti Angular frontend (see [`edit-issue-implementation-plan.md`](./edit-issue-implementation-plan.md))
**Status:** Requirements — the three highest-stakes product decisions are **confirmed** (see §5.1, §5.3, §11). Remaining items in §11 are lower-severity and carry recommended defaults.

This is a **standalone handoff**: it assumes no frontend context.

### Confirmed decisions (2026-07-22)
1. **Editable statuses:** `Rejected` + `Active` + `Submitted` + `UnderReview` (+ `Draft` iff reachable). See §5.1.
2. **Post-approval bait-and-switch guard:** all fields stay editable **and** the admin re-review screen must show a **field-level diff** of what changed vs the last-approved version. **The diff is a required v1 deliverable** (§5.3), not deferred.
3. **Live-issue visibility during re-review:** **pull-from-public (v1)** — an edited `Active` issue becomes non-public until re-approved (§5.3). A shadow-revision model is deferred.

---

## 1. Context & Goal

Civiti lets Romanian citizens submit civic issues that an admin moderates before they go public. Today an issue's creator can only edit **title, description, photos**, and only while the issue is **Rejected**. The goal: **the creator can edit *every* field of their own issue, from more places in the app, and doing so sends the issue back into the admin approval queue.**

The single load-bearing change: **`PUT /api/user/issues/{id}` must accept the full editable field set (it accepts only 4 fields today), enforce owner-only authorization, apply optimistic-concurrency, and transition the issue back to a pending-review status on resubmit.**

---

## 2. Affected Endpoint

### `PUT /api/user/issues/{id}`

- **Auth:** Required. Frontend sends a Supabase JWT as `Authorization: Bearer <token>`. Derive the caller from JWT `sub`. **The frontend performs NO authorization** — all ownership/status enforcement is backend-side.
- **Path param:** `id` — issue id (GUID/string).
- **Success:** `200 OK` with the **full updated `IssueDetailResponse`** (identical shape to `GET /api/issues/{id}`), reflecting the new `status` and bumped `updatedAt`. The client navigates using this payload and does not re-fetch.

**Related endpoints (context):**
- `GET /api/issues/{id}` → `IssueDetailResponse`, used to prefill the edit form. **See §9.4 — this must return a non-public issue to its owner.**
- `POST /api/issues` (`CreateIssueRequest`) — the edit request must reach **field & validation parity** with this.
- `GET /api/admin/pending-issues` — a resubmitted edit must reappear here (**§5.2, verify the filter**).
- `PUT /api/admin/issues/{id}/approve|reject|request-changes` — existing re-approval flow; no new admin endpoints needed.

---

## 3. Request Contract — Before / After

### 3.1 BEFORE (current)

```jsonc
// PUT /api/user/issues/{id} — CURRENT
{ "title": "string?", "description": "string?", "photoUrls": ["string"], "resubmit": true }
```

Missing: `category`, `address`, `district`, `latitude`, `longitude`, `urgency`, `desiredOutcome`, `communityImpact`, `authorities[]`, concurrency token.

### 3.2 AFTER (required)

Mirror the editable subset of `CreateIssueRequest`, plus `resubmit` and `expectedUpdatedAt`. Body is a **full replacement** of the editable fields (PUT), so validation can be shared with create.

```jsonc
// PUT /api/user/issues/{id} — REQUIRED
{
  "title":            "string",                 // required
  "description":      "string",                 // required
  "category":         "Infrastructure",         // required; IssueCategory (§3.4)
  "address":          "string",                 // required
  "district":         "string",                 // see §6 note on legacy nulls
  "latitude":         44.4268,                  // required number
  "longitude":        26.1025,                  // required number
  "urgency":          "medium",                 // optional; UrgencyLevel (§3.4)
  "desiredOutcome":   "string?",                // optional
  "communityImpact":  "string?",                // optional
  "photoUrls":        ["https://.../a.jpg"],    // optional; FULL replacement, ordered (index 0 = primary) — see §4 note
  "authorities": [                              // optional; FULL replacement (§7)
    { "authorityId": "uuid-of-predefined" },
    { "customName": "Primăria Sector 3", "customEmail": "contact@ps3.ro" }
  ],
  "resubmit":          true,                     // v1: always true (§5.4 / decision P4)
  "expectedUpdatedAt": "2026-07-21T09:00:00Z"   // optimistic concurrency (§9.3)
}
```

> **⚠ PUT vs PATCH — silent-null footgun.** Treat the body as a **complete replacement** of the editable fields. Do **not** let a client that omits `communityImpact` silently null a previously-set value unintentionally. Either (a) require the complete editable field set on every call (recommended, matches the client which always sends all fields), or (b) explicitly define and document omitted-vs-`null` semantics. Do **not** accept an all-optional partial and blank the rest.

#### Field constraints (parity — §6)

| Field | Type | Required | Notes |
|---|---|---|---|
| `title` | string | yes | Max 150 (create has no min; §6) |
| `description` | string | yes | Min 10 |
| `category` | `IssueCategory` | yes | §3.4 |
| `address` | string | yes | |
| `district` | string | yes* | Drives valid authorities. *§6: tolerate legacy null |
| `latitude`/`longitude` | number | yes | Sane range check |
| `urgency` | `UrgencyLevel` | no | Default `medium` |
| `desiredOutcome`/`communityImpact` | string | no | Max 1000 each |
| `photoUrls` | string[] | no | Full ordered replacement; index 0 = primary; ≥1 recommended, max 8 |
| `authorities` | `IssueAuthorityInput[]` | no | Full replacement; min 1, max 5 |
| `resubmit` | boolean | yes | §5 |
| `expectedUpdatedAt` | ISO string | recommended | §9.3 |

> **`whenOccurred` is NOT a backend field** — it's a UI-only create selector never sent to the backend. Do not add it.

### 3.3 `IssueAuthorityInput`

```jsonc
{ "authorityId": "string?",   // predefined; if set, custom* must be null
  "customName":  "string?",   // required if authorityId absent
  "customEmail": "string?" }  // required if authorityId absent
```
**Rule:** each entry is `authorityId` **XOR** (`customName` + `customEmail`). Reject entries providing both or neither.

### 3.4 Enums

- **`IssueCategory`**: `Infrastructure | Environment | Transportation | PublicServices | Safety | Other`
- **`UrgencyLevel`**: `unspecified | low | medium | high | urgent`
- **`IssueStatus`**: `Unspecified | Draft | Submitted | UnderReview | Active | Resolved | Rejected | Cancelled`

---

## 4. Response Contract

Return the full updated issue — **exact shape of `GET /api/issues/{id}` (`IssueDetailResponse`)**:

```jsonc
// 200 OK
{
  "id": "string", "title": "string", "description": "string",
  "category": "Infrastructure", "address": "string",
  "latitude": 44.4268, "longitude": 26.1025, "district": "string?",
  "urgency": "medium", "status": "Submitted",       // NEW status (§5)
  "emailsSent": 12, "communityVotes": 34,           // preserved counters (§5.3)
  "hasVoted": false,
  "desiredOutcome": "string?", "communityImpact": "string?",
  "createdAt": "2026-07-01T10:00:00Z",
  "updatedAt": "2026-07-21T09:30:00Z",              // MUST be bumped (also the next concurrency token)
  "photos": [ { "id": "string", "url": "https://...", "description": "string?", "isPrimary": true, "createdAt": "..." } ],
  "authorities": [ { "authorityId": "uuid?", "name": "string", "email": "string", "isPredefined": true } ],
  "user": { /* UserBasicResponse — the ORIGINAL creator; MUST be unchanged */ }
}
```

> **Photo request/response asymmetry + metadata loss (decision P6).** The request sends `photoUrls: string[]` (ordered, index 0 = primary); the response returns rich `photos` objects (`id`, `description?`, `isPrimary`). A flat `string[]` **cannot carry** per-photo `isPrimary`/`description` except positionally, and drops stable ids. If the product needs robust primary designation or photo descriptions preserved on edit, change the request to a richer input array `[{ url, isPrimary?, description? }]`. Otherwise accept positional-primary and document it.

---

## 5. Status / State-Machine Rules

### 5.1 Which statuses may the OWNER edit? (decision P1)

Replace the Rejected-only gate with an explicit set. **Recommended default:**

| Status | Owner-editable? | Rationale |
|---|---|---|
| `Draft` | Yes (if reachable — verify create can produce it) | Not yet submitted |
| `Rejected` | Yes | The original flow |
| `Submitted` | **Yes** | Fix right after submitting (§5.4) |
| `UnderReview` | **Yes** | Editable; content updated in place (§5.4) |
| `Active` | **Yes** | Live issue — the core new requirement |
| `Resolved` | **No** | Terminal |
| `Cancelled` | **No** | Terminal |
| `Unspecified` | **No** | Invalid |

Enforce server-side; reject non-editable statuses with **`409 Conflict`**. Do not rely on the client gate.

> **✅ CONFIRMED (decision P1):** this exact editable set is approved. `Active` and pending (`Submitted`/`UnderReview`) are editable. The frontend shows an Edit button for exactly this set, so `Submitted`/`UnderReview` must be genuinely editable (no "editable but blocked" state).

### 5.2 Transition on `resubmit = true`

When valid + authorized:
1. Persist all edited fields.
2. **Set `status`:**
   - from `Rejected` or `Active` → **`Submitted`**.
   - from `Submitted`/`UnderReview` (already pending) → **keep pending** (update content in place; no status change needed — it's already in the queue).
   - Landing status is **`Submitted`** (not `UnderReview`): `Submitted` = a fresh pending item matching the create flow's initial status; reserve `UnderReview` for "an admin actively picked it up". Admin "pending" bucket = `Submitted` ∪ `UnderReview`.
3. **The issue MUST appear in `GET /api/admin/pending-issues`.** ⚠ **Verify the queue filter includes the landing status** — if the queue filters on `UnderReview` only, resubmitted edits silently never reach admins.
4. **Clear stale moderation artifacts:** `rejectionReason`, `reviewedAt`, `reviewedBy`, prior `adminNotes`.
5. Bump `updatedAt`.
6. Write an audit/activity entry (§8).

### 5.3 Editing a LIVE `Active` issue — visibility, counters, and the bait-and-switch risk

- **Visibility — ✅ CONFIRMED pull-from-public (v1):** `Active` is public; `Submitted` is **not** (`PUBLIC_VIEWABLE_STATUSES = [Active, Resolved]`). Resubmitting an `Active` issue → `Submitted` → **pulls it from public until re-approved.** The frontend is built for this (it removes the issue from its public list slice when the new status isn't public). Implement exactly this.
  - ⚠ **Side effect (accepted for v1):** shared/QR/indexed links to that issue return not-found during re-review, and if the admin **rejects the edit** the previously-approved public version is gone (no rollback — no revision history exists). A shadow-revision model is the deferred long-term fix.
- **Counters — PRESERVE (decision P4):** `emailsSent`/`communityVotes` are server-owned; **do not reset** on edit (don't punish a typo fix). **Do NOT re-send or retract emails** on edit.
- **⚠ Bait-and-switch — ✅ CONFIRMED mitigation (decision P3): admin diff is REQUIRED for v1.** Preserved counters + full edit is only safe if the admin can see *what changed*. On resubmit of a previously-approved issue, the backend must expose enough to render a **field-level diff** (changed `title`/`description`/`category`/`location`/`authorities`/photos) on the admin re-review screen — e.g. return/persist the last-approved snapshot or a per-field `changedFields` set alongside the pending issue. This is **not deferred**; editing `Active` issues must not go live without it. (The reduced-field-set fallback is no longer in scope — full editability is approved *because* the diff ships.)

### 5.4 `resubmit` semantics and re-editing during review (decisions P4)

- **v1: `resubmit` is effectively always-on for owner edits.** Every owner edit of a non-`Draft` issue forces re-approval.
- **Forbid `resubmit=false` for any publicly-viewable status.** A silent edit of an `Active`/`Resolved` issue with no re-review is a **moderation bypass** (approve benign → silently edit to spam, stays live). If you keep a `resubmit=false` branch at all, restrict it to `Draft`. The v1 frontend never sends `resubmit=false`.
- **Editing while `Submitted`/`UnderReview`:** allowed — content is updated in place, status stays pending. Use optimistic concurrency (§9.3) to guard against clobbering a concurrent admin action, **not** a hard block (a hard block + a visible Edit button is a dead-end CTA).

---

## 6. Validation Parity with Create

Apply the **same rules `POST /api/issues` uses** (share the validator). Edit must never reject a value create accepted, or vice-versa.

| Field | Rule |
|---|---|
| `title` | Max **150**. (Create has **no** min-length; the old edit form wrongly enforced min 10 — align on create. Confirm if product wants a min.) |
| `description` | Required, min **10** |
| `category` | Required; valid `IssueCategory` |
| `address` / `district` | Required non-empty. **⚠ Legacy nulls:** `IssueDetailResponse.district` is nullable; a legacy/seeded issue may have a null `district`. Do not make an untouched null `district` unsavable — accept "keep as-is" or treat empty as valid for such issues. |
| `latitude` / `longitude` | Required numbers; sane range |
| `urgency` | If present valid `UrgencyLevel`; else default `medium` |
| `desiredOutcome` / `communityImpact` | Optional; max **1000** each |
| `photoUrls` | Max **8**; each a valid URL in the issue-photos bucket |
| `authorities` | Min **1**, max **5**; `authorityId` XOR (`customName`+`customEmail`); validate `customEmail` format |

On failure: `400` with `ErrorResponse { error, details?, requestId? }` for field-level surfacing.

---

## 7. Authorities Replacement Semantics (decision P5)

- **Full replace, not patch** — the `authorities` array is the complete desired set. Delete links no longer present, keep matching ones, add new ones.
- Predefined → `authorityId`; custom → `customName` + `customEmail`.
- **⚠ Email side-effects — the correctness landmine:** §5.3 requires "only *newly-added* authorities are emailed after re-approval," but a naive delete-and-recreate makes **every** link new → re-blasts everyone. **Track already-emailed authorities by a marker that survives the replace** (match by **email address**, or never hard-delete emailed links), so only genuinely new recipients are emailed. Never re-blast the full list on resubmit. Replacing the list must not send emails at edit time at all — emails go out only after admin re-approval.

---

## 8. Audit & Activity Logging

A user edit/resubmit is **not** an admin action and appears in no log today.
- **Audit log:** write an entry for resubmit capturing `previousStatus → newStatus` and the acting owner. The admin activity log's `actionType` is `approve | reject | requestchanges`; add an owner/system type such as `resubmit` so admins understand *why* an issue re-entered the queue.
- **Public activity feed:** `ActivityType` = `newSupporters | statusChange | issueApproved | issueResolved | issueCreated`. Optionally add `issueResubmitted` (or reuse `statusChange`). The frontend renders only what the API emits.
- If audit can't ship now, ship without it and note the gap — but a resubmit that silently reappears in the queue with no trail is a moderation smell.

---

## 9. Security, Concurrency & Error Responses

### 9.1 Ownership
- **Mandatory, server-enforced:** the issue's creator (`user.id`) must equal the authenticated caller → else **`403`** (or `404` to mask existence). Client gate is advisory.
- **Immutable:** ignore/reject any body attempt to change `user`/creator or `id`. Response `user` is always the original creator.

### 9.2 Status trust
- **Never accept a client-supplied `status`.** New status is derived server-side from `resubmit` + current status.
- Enforce the editable-status set (§5.1); reject terminal/ineligible → `409`.

### 9.3 Optimistic concurrency (required)
- The client sends `expectedUpdatedAt` (or use `If-Match` with an ETag/version). If the stored `updatedAt`/version differs → **`409 Conflict`** with a clear message; do **not** overwrite.
- **Why:** without it, an owner with a stale open form can overwrite a concurrent admin approve/reject (last-writer-wins over moderation). The client responds to `409` by reloading and telling the owner it changed.

### 9.4 Owner read of non-public issues (required contract)
- The edit form loads via `GET /api/issues/{id}`, which is auth-optional. After an edit an `Active` issue becomes `Submitted` (non-public). **This endpoint MUST return a non-public issue to its owner** (and to admins), and **MUST NOT** return a non-public issue's full content to anyone else (no info leak of `Rejected`/`Submitted` content to a stranger with the id). State and test this explicitly — the existing Rejected-only edit flow already depends on it but it's undocumented.

### 9.5 Auth failures
- Return `401` only for genuine auth failures (triggers the client refresh→retry→sign-out cascade); `403` for authenticated-but-not-owner.

### Error matrix

| Status | When | Body |
|---|---|---|
| `400` | Validation failure | `ErrorResponse { error, details?, requestId? }` |
| `401` | Missing/invalid/expired token | `ErrorResponse` |
| `403` | Authenticated but not creator | `ErrorResponse` |
| `404` | Issue not found (or to mask non-ownership) | `ErrorResponse` |
| `409` | Non-editable status **or** concurrency conflict (`expectedUpdatedAt` mismatch) | `ErrorResponse` with a clear reason |
| `200` | Success | Full `IssueDetailResponse` |

---

## 10. Acceptance Criteria

**Contract**
- [ ] Accepts `title, description, category, address, district, latitude, longitude, urgency, desiredOutcome, communityImpact, photoUrls, authorities, resubmit, expectedUpdatedAt`.
- [ ] Each field persisted and reflected in the response.
- [ ] `authorities` accepts predefined + custom and enforces the XOR rule.
- [ ] `photoUrls` treated as full ordered replacement; index 0 = primary; removed URLs unlinked **and their blobs GC'd from storage** (decision P6/§5.3 risk A4).
- [ ] Response is the full `IssueDetailResponse` with updated `status` and bumped `updatedAt`.

**Authorization & concurrency**
- [ ] Non-owner → `403`/`404`, no data modified.
- [ ] Creator/`user` cannot be changed via the body.
- [ ] Authorization from JWT, independent of the client.
- [ ] `expectedUpdatedAt` mismatch → `409`, no overwrite.
- [ ] Owner can read their own non-public issue via `GET /issues/{id}`; a stranger cannot (§9.4).

**State machine**
- [ ] Editable set enforced; `Resolved`/`Cancelled`/`Unspecified` → `409`.
- [ ] `resubmit=true` from `Rejected`/`Active` → `Submitted`; from pending → stays pending.
- [ ] Resubmitted issue appears in `GET /admin/pending-issues` (**filter verified**).
- [ ] Editing an `Active` issue removes it from public view until re-approved.
- [ ] Stale moderation fields cleared on resubmit.
- [ ] `resubmit=false` forbidden for publicly-viewable statuses.

**Data preservation & side-effects**
- [ ] `emailsSent`/`communityVotes` preserved (not reset).
- [ ] No emails sent/retracted on edit; after re-approval only **newly-added** authorities emailed (matched by email across the replace — no re-blast).

**Validation & audit**
- [ ] Validation matches create exactly; legacy null `district` doesn't block save.
- [ ] Validation failures → `400` with `ErrorResponse`.
- [ ] Resubmit writes an audit entry (`previousStatus → newStatus`, owner) — or the gap is documented.

**Admin loop**
- [ ] A resubmitted issue can be re-approved via `PUT /admin/issues/{id}/approve` and returns to `Active` / public.
- [ ] **(Required, decision P3)** For a resubmit of a previously-approved issue, the admin re-review screen can render a **field-level diff** vs the last-approved version (backend exposes a snapshot or `changedFields`); editing `Active` issues is not enabled in production until this is in place.

---

## 11. Decisions & remaining questions

**✅ Resolved (2026-07-22):**
1. **Editable status set (P1)** — `Rejected` + `Active` + `Submitted` + `UnderReview` (+ `Draft` iff reachable — **verify**). §5.1.
2. **Live-issue edit (P2/P3)** — accept v1 pull-from-public **and** ship the admin **diff view** (required, not deferred). §5.3.
3. **Post-approval editability** — **all** fields remain editable (no reduced set), guarded by the diff. §5.3.

**Still needs backend/product confirmation (lower severity):**
4. **Resubmit landing status** — `Submitted` (recommended) vs `UnderReview`? Must match the admin queue filter and client copy.
5. **`resubmit` (P4)** — always-on (recommended); confirm `resubmit=false` is dropped/forbidden for public statuses.
6. **Counters (P4)** — confirm PRESERVE `emailsSent`/`communityVotes`.
7. **Authorities after first approval (P5)** — freely replaceable with **email-marker de-dup** (recommended) so re-approval never re-blasts existing recipients, or locked?
8. **Primary photo / metadata (P6)** — is "index 0 = primary" acceptable, or add a richer photo input object (`[{url, isPrimary?, description?}]`)?
9. **Photo storage GC** — delete unlinked blobs immediately vs a sweep job?
10. **Concurrency token** — `expectedUpdatedAt` field vs `If-Match`/ETag — which fits your stack?
11. **Audit/activity** — add `resubmit`/`issueResubmitted` now or defer?
12. **Revision diff / history** — v1 has none (the diff in §5.3 works off a last-approved snapshot); is a full revision model on the roadmap? It's the clean long-term fix that would also stop live-issue edits from pulling public content.
