using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Civiti.Auth.Startup;

/// <summary>
/// Ensures every allow-listed MCP client described in <c>auth-design.md §6</c> has an
/// <see cref="OpenIddictApplicationDescriptor"/> registered in OpenIddict's application store.
/// Runs once at startup, idempotent on re-entry (updates existing entries in place).
///
/// Admin scopes (<c>civiti.admin.*</c>) are only granted to first-party Anthropic clients for
/// v1 — see the <c>allowsAdminScopes</c> gate in <c>auth-design.md §6</c>. Until DCR ships,
/// nothing outside this allow-list can request admin scopes at all.
/// </summary>
public sealed class ClientAllowListSeeder(
    IServiceProvider services,
    ILogger<ClientAllowListSeeder> logger) : BackgroundService
{
    // Retry cap + backoff. A single pass that fails on Railway cold start (DB not yet reachable)
    // would leave the allow-list empty until the pod restarts — and /authorize would return
    // "unknown client_id" in the meantime. We retry up to MaxAttempts with exponential backoff
    // (capped at MaxBackoff) before re-throwing; BackgroundService then logs the fatal exception
    // clearly rather than silently giving up.
    private const int MaxAttempts = 10;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    // BackgroundService (not IHostedService.StartAsync) so a slow or momentarily unreachable
    // DB doesn't block Kestrel from binding — the health endpoint must come up even during
    // a DB hiccup.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = InitialBackoff;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

                foreach (var client in AllowList)
                {
                    var existing = await manager.FindByClientIdAsync(client.ClientId, stoppingToken);
                    if (existing is null)
                    {
                        await manager.CreateAsync(client.ToDescriptor(), stoppingToken);
                        logger.LogInformation("Seeded OpenIddict application {ClientId}", client.ClientId);
                    }
                    else
                    {
                        await manager.UpdateAsync(existing, client.ToDescriptor(), stoppingToken);
                        logger.LogDebug("Updated OpenIddict application {ClientId}", client.ClientId);
                    }
                }

                return;
            }
            catch (OperationCanceledException)
            {
                return; // Host shutting down.
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex,
                    "Client allow-list seed attempt {Attempt}/{Max} failed; retrying in {Backoff}.",
                    attempt, MaxAttempts, backoff);

                try
                {
                    await Task.Delay(backoff, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoff = backoff * 2 < MaxBackoff ? backoff * 2 : MaxBackoff;
            }
            // No catch for the MaxAttempts iteration: the exception unwinds past the loop,
            // BackgroundService's host logger surfaces it, and the pod stays up (allow-list
            // empty, /authorize will reject until operator intervention).
        }
    }

    // Values track auth-design.md §6. The redirect URIs for claude.ai / cursor / chatgpt are
    // placeholders — the doc flags them as "confirm against each client's published docs before
    // launch", so they'll move in a follow-up PR once confirmed. Loopback URIs for native
    // Claude clients use RFC 8252's port-wildcard convention; OpenIddict's default exact-match
    // on redirect_uri will need a custom handler to strip the ephemeral port (see auth-design.md
    // §6 Dynamic Client Registration subsection). That handler lands alongside the DCR work.
    private static readonly Client[] AllowList =
    [
        new Client(
            ClientId: "claude-desktop",
            DisplayName: "Claude Desktop",
            RedirectUri: "http://127.0.0.1:0/callback",
            Scopes: ["civiti.read", "civiti.write", "civiti.admin.read", "civiti.admin.write"],
            AllowsAdminScopes: true),
        new Client(
            ClientId: "claude-code",
            DisplayName: "Claude Code",
            RedirectUri: "http://127.0.0.1:0/callback",
            Scopes: ["civiti.read", "civiti.write", "civiti.admin.read", "civiti.admin.write"],
            AllowsAdminScopes: true),
        new Client(
            ClientId: "claude-ai",
            DisplayName: "Claude (claude.ai)",
            RedirectUri: "https://claude.ai/api/mcp/auth_callback",
            Scopes: ["civiti.read", "civiti.write"],
            AllowsAdminScopes: false),
        new Client(
            ClientId: "cursor",
            DisplayName: "Cursor",
            RedirectUri: "cursor://anysphere.cursor-retrieval/callback",
            Scopes: ["civiti.read", "civiti.write"],
            AllowsAdminScopes: false),
        new Client(
            ClientId: "chatgpt-connector",
            DisplayName: "ChatGPT",
            RedirectUri: "https://chatgpt.com/connector_platform_oauth_redirect",
            Scopes: ["civiti.read"],
            AllowsAdminScopes: false)
    ];

    private sealed record Client(
        string ClientId,
        string DisplayName,
        string RedirectUri,
        string[] Scopes,
        bool AllowsAdminScopes)
    {
        public OpenIddictApplicationDescriptor ToDescriptor()
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = ClientId,
                DisplayName = DisplayName,
                // Native/public clients: no client secret, PKCE enforces binding.
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Explicit
            };

            descriptor.RedirectUris.Add(new Uri(RedirectUri, UriKind.Absolute));

            descriptor.Permissions.UnionWith(
            [
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code
            ]);

            foreach (var scope in Scopes)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
            }

            // Custom property marker; used when admin-scope gating ships in v1b/v1c to reject
            // civiti.admin.* requests from clients without this flag. OpenIddict round-trips
            // extension properties through its application manager.
            descriptor.Properties["civiti.allows_admin_scopes"] =
                System.Text.Json.JsonDocument.Parse(AllowsAdminScopes ? "true" : "false").RootElement;

            return descriptor;
        }
    }
}
