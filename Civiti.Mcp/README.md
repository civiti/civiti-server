# Civiti.Mcp

**Status:** Design phase — not yet implemented.

This directory is the home of the Civiti MCP ([Model Context Protocol](https://modelcontextprotocol.io))
server: a separate ASP.NET service that will expose a curated subset of Civiti's
domain as MCP tools and resources, so citizens and admins can interact with the
platform from any MCP-compatible AI client (Claude Desktop, Claude Code, Cursor,
ChatGPT connectors, etc.).

No code or `.csproj` has been added yet. This PR contains design documents only,
so we can align on scope, auth, and tool surface before writing any C#.

## Auth model at a glance

Civiti's MCP uses **OAuth 2.1** (via OpenIddict) for authenticated access and a
separate anonymous endpoint for public civic-data browsing. **There are no
API keys.** Users connect their Claude client by pasting a URL into the
standard connector UI; a browser-based login + consent flow handles
everything. See [`docs/connecting-civiti-to-claude.md`](docs/connecting-civiti-to-claude.md)
for the end-user experience and [`docs/auth-design.md`](docs/auth-design.md)
for the full design.

For v1 the only documented clients are Anthropic's: Claude Desktop,
Claude Code, claude.ai.

## Design documents

| Doc | Purpose |
| --- | --- |
| [`docs/architecture.md`](docs/architecture.md) | Service boundaries, transport, deployment, observability, shared-library plan. |
| [`docs/auth-design.md`](docs/auth-design.md) | How MCP clients authenticate against Supabase — three services, OpenIddict, no persisted upstream tokens, OAuth 2.1 with DCR guardrails. |
| [`docs/tool-inventory.md`](docs/tool-inventory.md) | Enumerated tools and resources per persona (public / citizen / admin), with input schemas, backing services, rate-limit classes, and audit tags. |
| [`docs/connecting-civiti-to-claude.md`](docs/connecting-civiti-to-claude.md) | End-user walkthrough: what a Civiti user does to connect Claude Desktop / Claude Code / claude.ai to their account. |

## Prerequisite: shared-library refactor

Before the MCP server can be implemented, the domain / services / data layers
currently inside `Civiti.Api` must be extracted into standalone class libraries
so `Civiti.Api`, `Civiti.Mcp`, and `Civiti.Auth` can all consume them
without duplicating business rules.

That refactor is a pure mechanical extraction with no behavior change, and will
land in its own PR ahead of any MCP code. See
[`docs/architecture.md#library-extraction-prerequisite`](docs/architecture.md#library-extraction-prerequisite)
for the proposed split.

## Review flow

1. Review these four design docs.
2. Iterate on auth and tool inventory in particular — those are the two
   highest-risk decisions.
3. Once approved, open the library-extraction PR.
4. Once that merges, start the `Civiti.Mcp` skeleton PR (project scaffolding +
   2–3 read-only tools wired end-to-end against Claude Desktop as the smoke
   test).
