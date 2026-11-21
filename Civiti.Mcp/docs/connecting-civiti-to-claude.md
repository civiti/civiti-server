# Connecting a client to Civiti

**Setting this up as a citizen?** The setup guide lives on [civiti.ro/ghid](https://civiti.ro/ghid). It is being added there in civiti/civiti-web#84, and this repo doesn't keep a separate user guide.

The rest of this page is a summary for developers. The production URL is in the [technical reference](../README.md#production-url).

## What a client needs

- **MCP over Streamable HTTP.** The server is stateless, so there is no session to manage.
- **For `/mcp`: OAuth 2.1 with PKCE and Dynamic Client Registration.** There are no API keys. Clients discover everything from the server, so the MCP URL is the only thing a user provides.
- **For `/mcp/public`: nothing.** No account, no sign-in. It lists every tool, but only the public ones work.

## What happens when a client connects to `/mcp`

1. The client calls `/mcp` without a token and gets a `401` pointing at `/.well-known/oauth-protected-resource`.
2. That document names Civiti.Auth, and the client reads its OAuth metadata.
3. The client registers itself at `/register`.
4. The user signs in with Google or email and password, and approves access on the consent screen.
5. The client exchanges the code for a 15-minute access token and a refresh token, then calls `/mcp` with the access token.

[`auth-design.md`](auth-design.md) has the full design.

## Adding the server

For claude.ai and Claude Desktop, add a custom connector with the `/mcp` URL. Both are verified end to end.

For Claude Code, which hasn't been verified with its own sign-in yet:

```bash
claude mcp add --transport http civiti <production URL>/mcp
```

See [Supported clients](../README.md#supported-clients) for the other clients, and [Troubleshooting](../README.md#troubleshooting) if a connection fails.
