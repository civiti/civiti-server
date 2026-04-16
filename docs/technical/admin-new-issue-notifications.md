# Admin-on-New-Issue Email Notifications

When a citizen submits a new civic issue, every admin is notified by email so
they can review and approve (or reject) it.

## Design summary

| Concern | Decision |
| --- | --- |
| Admin source of truth | Supabase `auth.users.raw_app_meta_data.role == "admin"`. **No parallel admin table or env allowlist.** Manual SQL grants (`UPDATE auth.users SET raw_app_meta_data = raw_app_meta_data ‖ '{"role":"admin"}'::jsonb WHERE email = '…'`) remain the only way to grant admin. |
| Request-path impact | Zero. `POST /api/issues` returns to the caller before the notifier does any network work. The in-process enqueue is non-blocking and drop-write. |
| Transport | Existing `Channel<EmailNotification>` + `EmailSenderBackgroundService` (Resend). A dedicated `Channel<AdminNotifyRequest>` drives the fanout logic. |
| Idempotency | `AdminIssueNotifications` table with composite PK `(IssueId, AdminEmail)`. Before sending to an admin we check/insert an audit row; a retry or racing worker can't double-send. |
| Admin list freshness | Cached in memory for `AdminListCacheSeconds` (default 60s). A burst of submissions doesn't hammer the Supabase Admin API. |
| Retry policy | Supabase Admin API calls retry on 5xx / timeout with exponential backoff (`MaxSupabaseRetries`, default 3). Email delivery retry is delegated to the existing email pipeline. |
| Failure semantics | All errors are logged, never thrown. A broken notifier never fails issue creation. |

## Pipeline

```
POST /api/issues              ─▶ IssueService.CreateIssueAsync
                                    │
                                    │  (after transaction commit)
                                    ▼
                                IAdminNotifier.NotifyNewIssueAsync(issueId)
                                    │
                                    │  enqueue AdminNotifyRequest
                                    ▼
                                Channel<AdminNotifyRequest>
                                    │
                                    ▼
                                AdminNotifyBackgroundService
                                    │
                                    ├── load Issue (+ author) from DB
                                    ├── ISupabaseAdminClient.ListAdminsAsync()  ← cached 60s
                                    │       └── GET {SUPABASE_URL}/auth/v1/admin/users?page=…&per_page=…
                                    │               apikey + Authorization: Bearer {SERVICE_ROLE_KEY}
                                    │
                                    ├── for each admin:
                                    │     ├── INSERT (IssueId, email) into AdminIssueNotifications
                                    │     │     (skip if already present — per-recipient idempotency)
                                    │     └── enqueue EmailNotification → Channel<EmailNotification>
                                    │
                                    ▼
                                EmailSenderBackgroundService → Resend → admin inbox
```

## Environment variables

| Variable | Default | Purpose |
| --- | --- | --- |
| `ADMIN_NOTIFY_ENABLED` | `true` in prod, `false` in dev | Feature flag. When `false`, new issues are not announced to admins. |
| `ADMIN_NOTIFY_CHANNEL_CAPACITY` | `1000` | Bounded in-process queue. Drops on overflow (logged). |
| `ADMIN_NOTIFY_CACHE_SECONDS` | `60` | In-memory TTL for the admin list. |
| `ADMIN_NOTIFY_MAX_RETRIES` | `3` | Retries for transient Supabase Admin API failures. |
| `ADMIN_NOTIFY_SUPABASE_TIMEOUT_SECONDS` | `10` | Per-request timeout for Supabase Admin API calls. |
| `ADMIN_NOTIFY_SUPABASE_PAGE_SIZE` | `200` | Page size when listing Supabase users. |
| `SUPABASE_SERVICE_ROLE_KEY` | *(empty)* | Required for admin listing. Backend-only — never exposed to the frontend. |

Already-existing settings consumed by this feature:

- `RESEND_API_KEY` / `Resend:ApiKey`
- `RESEND_FROM_EMAIL` / `Resend:FromEmail`
- `RESEND_FRONTEND_BASE_URL` / `Resend:FrontendBaseUrl` — deep-link base (`/admin/issues/{id}`).

## Email content

Romanian template (`EmailNotificationType.AdminNewIssue`):

- **Subject**: `Nouă problemă raportată: {title}`
- **Body** (plain language): a short intro plus a labeled table with Title,
  Category, Address (with district when present), Urgency, and submitter name.
- **CTA**: `{FRONTEND_BASE_URL}/admin/issues/{id}` → "Deschide în panoul admin"

## Operational notes

- **Granting admin**: the SQL-on-Supabase workflow documented in the design brief
  remains unchanged. This feature only *reads* `app_metadata.role` — it never writes.
- **Feature flag off**: `AdminNotifier` short-circuits before writing to the channel,
  and the `AdminNotifyBackgroundService` is not registered. Safe to disable in dev.
- **Service role key missing**: `SupabaseAdminClient` logs a warning and returns an empty
  admin list. No emails are sent; issue creation is unaffected.
- **Back-pressure**: both the `AdminNotifyRequest` channel and the `EmailNotification`
  channel are bounded + drop-write. We'd rather miss a notification than wedge request
  handling or blow memory. Drops are logged with an actionable "increase capacity" hint.

## Future-proofing

- `AdminNotifyRequest.Type` is already a discriminator — adding a new event type
  (issue status change, abusive content flagged, etc.) is an enum entry + a new branch
  in `ProcessRequestAsync` (or ideally a small strategy per type). No new channel.
- If Supabase Admin API rate-limiting ever becomes an issue, swap
  `ISupabaseAdminClient` for a local `Admin` table sync job. Call sites don't change.
