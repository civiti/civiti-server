# Sitemap Data Strategy — Decision Record

**Date:** 2026-04-09
**Status:** Accepted
**Context:** SEO / sitemap.xml generation for the Vercel-hosted Civiti frontend

## Background

The Civiti frontend needs to generate a dynamic `sitemap.xml` (via a Vercel serverless
function) that lists every publicly-visible issue with an accurate `<lastmod>` so
search engines can crawl the site efficiently and notice when an issue page changes.

The frontend sitemap handler fetches data from the backend. Three options were
considered for how the backend should expose that data:

1. **(a)** Add an `UpdatedAt` field to the existing `IssueListResponse` returned by
   `GET /api/issues`. The sitemap function paginates through the existing endpoint
   (bounded by the `pageSize=100` cap) and uses `UpdatedAt` for `<lastmod>`.
2. **(b)** Build a new dedicated endpoint `GET /api/issues/sitemap` that returns a
   flat, unpaginated `[{ id, updatedAt }]` for every publicly-visible issue — no
   joins, no heavy fields, no pagination.
3. **(c)** Lift the `pageSize` cap on `GET /api/issues` (currently clamped to 100)
   so the sitemap function can fetch everything in one request.

## Decision

**Adopt option (a).** Ship only the `UpdatedAt` field addition. Do **not** build a
dedicated sitemap endpoint and do **not** lift the `pageSize` cap.

## Rationale

### Why (a)

- **Small, backwards-compatible change.** Adds a single field to an existing response
  DTO and projection. No migration, no breaking change for existing callers (mobile
  app, Angular frontend).
- **`UpdatedAt` is already maintained** on the `Issue` entity and is genuinely bumped
  by meaningful events: admin approve/reject/status-change (`AdminService`), email-
  sent increments and votes (`IssueService`). The `<lastmod>` hint it produces will
  therefore reflect real activity, not just creation time.
- **Pagination is fine at current scale.** The Bucharest pilot is expected to sit in
  the tens-to-low-hundreds of public issues. At `pageSize=100`, that's 1–3 requests
  per sitemap rebuild. The sitemap function is behind a 1-hour edge cache
  (`s-maxage=3600, stale-while-revalidate=86400`), so the backend takes at most one
  rebuild's worth of requests per hour regardless of crawl volume.

### Why *not* (b) — yet

A dedicated `GET /api/issues/sitemap` endpoint is cleaner in theory but not worth the
cost *today*:

- **Duplicated invariant.** The service already enforces a "publicly visible =
  `Active` or `Resolved`" filter in exactly one place (`IssueService.GetAllIssuesAsync`,
  see the `allowedPublicStatuses` array). A second endpoint means that invariant lives
  in two places, and any future status additions need to be mirrored or we risk
  leaking drafts/rejected/cancelled issues into the sitemap.
- **Second surface to maintain.** Docs, tests, OpenAPI, authorization policy,
  rate-limit config — all duplicated for a caller that runs at most once per hour.
- **Speculative optimization.** There is currently no evidence the paginated path is
  too slow or too heavy for the sitemap use case.

### Why *not* (c)

- **The `pageSize=100` cap is a general safety rail**, not an arbitrary number. The
  list endpoint does `.Include(i => i.Photos)` and projects 15+ fields per row.
  Raising the cap globally to accommodate one caller means every other consumer
  (including abusive ones) gets the bigger blast radius for free.
- **Wrong layer to optimize.** If pagination ever becomes a real pain point for the
  sitemap function, the right answer is (b) — a dedicated lightweight endpoint — not
  relaxing the cap on the general-purpose list endpoint.

## When to revisit (triggers for option (b))

Promote to the dedicated `GET /api/issues/sitemap` endpoint if **any** of the
following becomes true:

- **Scale:** public issue count exceeds roughly **1,000** and the pagination loop
  requires more than ~10 round trips per sitemap rebuild.
- **Latency:** the sitemap serverless function's p95 latency to rebuild the XML
  approaches or exceeds its Vercel timeout (currently ~5s).
- **Payload cost:** backend logs show the sitemap rebuild transferring a
  disproportionately large payload relative to what the sitemap actually consumes
  (everything except `id` and `updatedAt` is waste for this caller).
- **Cache miss pressure:** the hourly edge cache starts getting bypassed frequently
  (e.g., multiple edge regions cold), so the backend starts seeing significant
  sitemap load.

When revisiting, the dedicated endpoint should:

- Return `List<{ Id: Guid, UpdatedAt: DateTime }>` — no pagination, no photos, no
  includes.
- Reuse the existing `allowedPublicStatuses` definition from `IssueService` rather
  than re-declaring the filter (extract to a shared helper if needed).
- Be anonymous (no auth) and exempted from per-user filters like blocked-user
  hiding — the sitemap is the same for everyone.
- Be documented in `docs/api/endpoints/issues.md` (create if missing).

## Related changes

- `Civiti.Api/Models/Responses/Issues/IssueListResponse.cs` — added `UpdatedAt`
- `Civiti.Api/Services/IssueService.cs` — projected `UpdatedAt` in
  `GetAllIssuesAsync`
- `Civiti.Api/Infrastructure/Configuration/SwaggerExamples.cs` — updated example
