# Civiti.Mcp — Railway deployment

> **Status:** How the `Civiti-MCP` service is set up. It lives in the same Railway
> project as `Civiti-Server` and `Civiti-Auth`, sharing the Postgres instance. For
> what the service exposes, see the [technical reference](../../Civiti.Mcp/README.md).

## Service topology

```
Railway project: civiti-server (shared)
├── Civiti-Server     ← Civiti.Api, the REST host; runs migrations
├── Civiti-MCP        ← Civiti.Mcp, the MCP Resource Server; /mcp and /mcp/public
├── Civiti-Auth       ← Civiti.Auth, the OAuth 2.1 Authorization Server
└── Postgres          ← shared database
```

Per [`Civiti.Mcp/docs/architecture.md §2`](../../Civiti.Mcp/docs/architecture.md): three processes, one solution, one database, one shared domain/application layer. Separate Railway *services*, not separate Railway *projects* — intra-project service references (`${{Postgres.DATABASE_URL}}`) and deploy graph dependencies are the reason.

## Dashboard setup

**Step order matters.** Connecting the GitHub source triggers an immediate first deploy; if the Config Path isn't set yet, Railway falls back to the repo-root `railway.json` and builds `Civiti.Api` into the new service. Create the service *empty*, configure it, *then* connect the source.

1. **Create empty service.** Railway project → **+ New** → **Empty Service**. Name it `Civiti.Mcp`.
2. **Set Config Path first.** Service **Settings → Config-as-code Path**: `Civiti.Mcp/railway.json`. That file points at `Civiti.Mcp/Dockerfile` and sets `/api/health` as the healthcheck. Doing this before any source is connected means the first build picks up the right config.
3. **Env vars.** Set under **Variables** (before first deploy so the build has them):
   | Variable | Value | Source |
   | --- | --- | --- |
   | `DATABASE_URL` | `${{Postgres.DATABASE_URL}}` | Service reference to the shared Postgres instance |
   | `ASPNETCORE_ENVIRONMENT` | `Production` | Static |
   | `CIVITI_AUTH_ISSUER` | The Civiti-Auth URL, e.g. `https://<auth-domain>/` | The issuer whose tokens `/mcp` accepts |
   | `CIVITI_MCP_PUBLIC_ORIGIN` | This service's public scheme and host, no path | Builds the OAuth discovery document |
   | `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEY` | Same values as `Civiti-Server` | Required by the shared services |
   | `OPENAI_API_KEY` | Same value as `Civiti-Server` | Content moderation. Required outside Development |
   | `CLAUDE_API_KEY` | Same value as `Civiti-Server` | Optional. `generate_petition_body` uses the template without it |
   | `PORT` | *(do not set — Railway injects automatically)* | — |
   `Program.cs` reads `PORT` and binds `0.0.0.0:$PORT`; if `DATABASE_URL` arrives in the URI format (`postgresql://…`) it's converted to the Npgsql key/value form inline, with SSL required.
4. **Connect GitHub source.** Service **Settings → Source → Connect Repo** → `civiti/civiti-server` → branch `master`. This triggers the first build, which now uses the Config Path from step 2 and the env vars from step 3.
5. **Public domain.** Service **Settings → Domains → Generate Domain**. Production uses the generated domain; the URL is recorded [in the technical reference](../../Civiti.Mcp/README.md#production-url). Moving to a custom domain also means updating `CIVITI_MCP_PUBLIC_ORIGIN`, which clients read from the discovery document.
6. **Deploy-order dependency.** Service **Settings → Deploy Triggers** → add a dependency on the `Civiti.Api` service. This ensures schema migrations (run by Civiti.Api per [`architecture.md §3`](../../Civiti.Mcp/docs/architecture.md#library-extraction-prerequisite)) complete before Civiti.Mcp re-deploys. Civiti.Mcp never runs migrations itself.

**If you connected the repo before setting the Config Path by mistake:** the first build will fail or deploy `Civiti.Api`'s binary into this service. Set the Config Path (step 2), then **Deployments → Redeploy** to rebuild with the correct Dockerfile. No data loss — this is just a cold start.

## Verification

Once the first deploy settles:

- `GET https://<domain>/api/health` → `200 { "status": "Healthy", "database": "connected" }`. Health returns `503` (not `200`) if the DB is unreachable, so Railway's healthcheck gate works as expected.
- `POST https://<domain>/mcp/public` with an MCP `tools/list` request returns every tool in [`tool-inventory.md`](../../Civiti.Mcp/docs/tool-inventory.md). Easiest smoke test: add a custom connector in Claude Desktop pointing at the public URL — no OAuth flow runs on `/mcp/public`.
- `POST https://<domain>/mcp` without a token returns `401` with a `WWW-Authenticate` header pointing at `/.well-known/oauth-protected-resource`.

## Known limitations to address in follow-ups

1. **Migration ordering is dashboard-configured, not code-enforced.** [`architecture.md §3`](../../Civiti.Mcp/docs/architecture.md#library-extraction-prerequisite) describes a planned `Civiti.Api --migrate-only` pre-deploy command plus a compiled-in `RequiredMigrationId` floor check on the other hosts. Neither exists yet.
2. **No Grafana / external metrics.** Observability is stdout → Railway logs only.
