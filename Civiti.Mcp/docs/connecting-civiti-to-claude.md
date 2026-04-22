# Connecting Civiti to your Claude client

> **Audience:** Civiti users — citizens and admins — who want to use
> Claude Desktop, Claude Code, or claude.ai to interact with Civiti.
>
> **Status:** This guide describes the intended end-user experience.
> Civiti MCP is not yet live; links and URLs below are placeholders.

You will **not** need an API key. Civiti uses standard OAuth 2.1 — the same pattern Claude uses for Google Drive, GitHub, Slack, and every other connector.

## Before you start

You need:

- A **Civiti account** (sign up at civiti.app if you don't have one).
- **Claude Desktop**, **Claude Code**, or access to **claude.ai's custom connectors**.

If you only want to browse civic data anonymously — for example, to research issues in a city before deciding whether to join — see [Anonymous access](#anonymous-access) at the bottom. You can ask Claude "show me reported issues in Cluj" and draft a petition without signing up.

## Claude Desktop

1. Open **Claude Desktop → Settings → Connectors → Add Custom Connector**.
2. Paste the server URL:
   - Full access (Civiti account required): `https://mcp.civiti.app/mcp`
   - Anonymous read-only: `https://mcp.civiti.app/mcp/public`
3. Click **Connect**. Your default browser opens to a Civiti login page.
4. Log in with your Civiti credentials (Google or email/password — whatever you normally use on civiti.app).
5. Review the **consent screen**:
   - **Read access** — Claude can see your profile, issues you've reported, your activity.
   - **Write access** — Claude can submit issues, vote, and comment on your behalf.
   - Toggle off any scope you don't want to grant. Read-only is a valid choice.
   - Notice: data returned by Civiti tools flows to Anthropic per their terms.
6. Click **Approve**. The browser redirects back to Claude Desktop.
7. Claude Desktop shows **Civiti connected** in its Connectors list.

That's it. Claude remembers the connection across restarts. After 30 days you'll be asked to re-consent; the re-consent is a single click if your Civiti session is still valid.

## Claude Code

```bash
claude mcp add civiti --transport http https://mcp.civiti.app/mcp
```

The OAuth flow runs on your first tool call — a browser opens, you log in to Civiti, you consent, you're done. Claude Code stores the tokens in your OS keychain.

## claude.ai (web)

Custom connectors in claude.ai follow the same pattern: paste the server URL, log in to Civiti, consent, connected.

## What you can ask Claude once connected

- "Show me issues in my neighborhood."
- "What issues near me are most urgent?"
- "Draft a petition about the pothole on Str. Traian and walk me through sending it."
- "Submit a new issue about the broken streetlight on …" *(requires write access)*
- "What's my Civiti activity this month?"

The full set of capabilities is in [`tool-inventory.md`](tool-inventory.md).

## For Civiti admins

If you are a Civiti admin and want to use Claude for moderation work (reviewing pending issues, approving / rejecting, bulk-approving), two extra conditions apply:

1. **`McpAdminAccessEnabled` must be turned on for your account.** A super-admin flips this flag in the admin UI (it is off by default for every admin). This is an explicit, account-level opt-in to using MCP for privileged actions — it exists so that a compromised admin account cannot be escalated into MCP-mediated moderation without a human turning the flag on.
2. **Use Claude Desktop or Claude Code only.** For v1, admin scopes (`civiti.admin.read`, `civiti.admin.write`) are only offered to Anthropic's first-party clients. Other MCP clients (Cursor, ChatGPT connectors, DCR-registered third parties) can still connect, but the admin checkboxes will not appear on the consent screen.
3. **Grant admin scopes explicitly.** When you connect, the consent screen shows admin scopes as separate, opt-in toggles. Approve them only for the clients where you intend to do moderation work. You can revoke them at any time from civiti.app → Settings → Connected AI Assistants.

**Important moderation safety note:** destructive admin actions (approve, reject, bulk-approve, request-changes) use a two-step confirmation flow — Claude first calls a `propose_*` tool, and a second tool call (`confirm_admin_action`) within 5 minutes commits the change. This is a deliberate defense against prompt-injection: if a pending issue's description contains hostile text that tries to manipulate Claude into approving itself, the two-step confirmation gives you the chance to catch it before anything changes. Every admin tool call also fires an email to your inbox immediately.

## Managing and revoking access

Everything is controlled from **civiti.app → Settings → Connected AI Assistants**:

- See every Claude client currently connected to your account.
- See what scopes each one has and when it was last used.
- **Revoke** any connection with one click. The client loses access immediately.

Revoke is the right move if you:

- Lose the device Claude was installed on.
- Suspect your Claude account is compromised.
- Simply want to stop using the integration.

You can always reconnect later by repeating the setup flow.

## Anonymous access

Want to use Claude to browse Civiti without creating an account? Use the public endpoint:

- URL: `https://mcp.civiti.app/mcp/public`
- No login, no consent screen, no account needed.
- **Read-only:** you can search issues, view details, list local authorities, and mark that you've sent a petition email (Civiti counts it; the email itself you send from your own inbox).
- Rate-limited per source IP — heavy, automated use is not supported on this endpoint.

This mirrors what you'd see visiting civiti.app as a logged-out visitor. To actually report an issue, vote, or comment, you need a Civiti account and the authenticated endpoint.

## Why no API key?

A common pattern for other MCP servers is to paste an API key into a config file. Civiti does it differently on purpose:

- **Your identity already exists in Civiti.** A Claude-specific key would be redundant.
- **Scope control at connect time.** OAuth lets you grant Claude a narrower permission set (e.g. read-only) than your full account. An API key typically grants everything your account can do.
- **Nothing reusable leaks.** A stolen config file contains only a short-lived access token that expires in 15 minutes, not a permanent credential.
- **One-click revoke.** You can kill Claude's access without changing your Civiti password or regenerating anything.

## Troubleshooting

*(To be expanded once the service is live. Common cases will go here: browser-redirect failures, stalled OAuth flows, token expiry, revoked sessions, DCR rate-limit hits.)*
