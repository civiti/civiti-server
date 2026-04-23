# Civiti.Mcp — Railway deployment

> **Status:** First deployment of the v0 `/mcp/public` endpoint. Lives in the
> same Railway project as `Civiti.Api`, sharing the Postgres instance.
> Civiti.Auth (v1) and the authenticated `/mcp` endpoint are separate future services.

## Service topology

```
Railway project: civiti (shared)
├── Civiti.Api        ← existing REST host; runs migrations
├── Civiti.Mcp  NEW   ← MCP Resource Server; /mcp/public anonymous endpoint
└── Postgres          ← shared database
```

Per [`Civiti.Mcp/docs/architecture.md §2`](../../Civiti.Mcp/docs/architecture.md): three processes, one solution, one database, one shared domain/application layer. Separate Railway *services*, not separate Railway *projects* — intra-project service references (`${{Postgres.DATABASE_URL}}`) and deploy graph dependencies are the reason.

## Dashboard setup

1. **Create service.** Railway project → **+ New** → **GitHub Repo** → select `civiti/civiti-server` → **Deploy**.
2. **Config path.** Service **Settings → Config-as-code Path**: `Civiti.Mcp/railway.json`. That file points at `Civiti.Mcp/Dockerfile` and sets `/api/health` as the healthcheck.
3. **Env vars.** Set under **Variables**:
   | Variable | Value | Source |
   | --- | --- | --- |
   | `DATABASE_URL` | `${{Postgres.DATABASE_URL}}` | Service reference to the shared Postgres instance |
   | `ASPNETCORE_ENVIRONMENT` | `Production` | Static |
   | `PORT` | *(do not set — Railway injects automatically)* | — |
   `Program.cs` reads `PORT` and binds `0.0.0.0:$PORT`; if `DATABASE_URL` arrives in the URI format (`postgresql://…`) it's converted to the Npgsql key/value form inline. No other v0 vars are required — Supabase creds are only needed from v1 onward.
4. **Public domain.** Service **Settings → Domains → Generate Domain**. For production, add a custom domain (e.g. `mcp.civiti.app`) and point the DNS CNAME at the Railway-provided target. The connector URL users paste into Claude is `https://<domain>/mcp/public`.
5. **Deploy-order dependency.** Service **Settings → Deploy Triggers** → add a dependency on the `Civiti.Api` service. This ensures schema migrations (run by Civiti.Api per [`architecture.md §3`](../../Civiti.Mcp/docs/architecture.md#3-library-extraction-prerequisite)) complete before Civiti.Mcp re-deploys. Civiti.Mcp never runs migrations itself.

## Verification

Once the first deploy settles:

- `GET https://<domain>/api/health` → `200 { "status": "Healthy", "database": "connected" }`. Health returns `503` (not `200`) if the DB is unreachable, so Railway's healthcheck gate works as expected.
- `POST https://<domain>/mcp/public` with an MCP `initialize` request returns a tool list containing the six §1 tools (`search_issues`, `get_issue`, `list_authorities`, `get_categories`, `get_leaderboard`, `mark_email_sent`). Easiest smoke test: add a custom connector in Claude Desktop pointing at the public URL — no OAuth flow runs on `/mcp/public`.

## Known limitations to address in follow-ups

1. **ForwardedHeaders proxy trust.** `Program.cs` clears `KnownIPNetworks` + `KnownProxies` to match `Civiti.Api`'s current config, so `X-Forwarded-For` is trusted from any upstream. On Railway this is bounded by the edge appending (not overwriting) the header chain, but a header-rotating attacker reaching the container can still inflate IP-keyed rate-limit counters. Once we've observed the live `X-Forwarded-For` chain from this deployed service, replace the clears with explicit `KnownNetworks` covering Railway's forward range — applies to both `Civiti.Mcp` and `Civiti.Api`.
2. **Migration ordering is dashboard-configured, not code-enforced.** [`architecture.md §3`](../../Civiti.Mcp/docs/architecture.md#3-library-extraction-prerequisite) describes a planned `Civiti.Api --migrate-only` pre-deploy command plus a compiled-in `RequiredMigrationId` floor check on the other hosts. Neither exists yet. For v0 that's acceptable — the schema hasn't changed; but the floor-check belt-and-braces lands when Civiti.Auth introduces `McpSessions`.
3. **No Grafana / external metrics.** Observability is stdout → Railway logs only. Revisit with the rest of the observability story when Civiti.Auth ships.
