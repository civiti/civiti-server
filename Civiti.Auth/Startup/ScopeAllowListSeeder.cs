using OpenIddict.Abstractions;

namespace Civiti.Auth.Startup;

/// <summary>
/// Persists the four civiti scopes (<c>civiti.read</c>, <c>civiti.write</c>,
/// <c>civiti.admin.read</c>, <c>civiti.admin.write</c>) into OpenIddict's scope manager with
/// their <see cref="OpenIddictScopeDescriptor.Resources"/> populated from
/// <see cref="McpResources"/>.
///
/// <para>
/// Why this exists: OpenIddict 7.5's <c>Authentication.ValidateResources</c> handler rejects
/// any RFC 8707 <c>resource</c> parameter on <c>/authorize</c> with <c>invalid_target</c>
/// (<c>ID2190</c>) unless the URL appears in the <c>Resources</c> collection of at least one
/// requested scope. Claude Desktop's connector posts
/// <c>resource=https://civiti-mcp-development.up.railway.app/mcp</c> per the
/// protected-resource discovery doc; without this seeder, every Connect attempt fails before
/// reaching the consent screen.
/// </para>
///
/// <para>
/// The audience claim on issued JWTs is still pinned to <see cref="Civiti.Application.Mcp.McpResourceIdentifiers.Audience"/>
/// via the explicit <c>principal.SetResources(...)</c> call in <c>AuthorizeEndpoint</c>, so
/// Civiti.Mcp's validator (which expects the constant <c>"civiti-mcp"</c>) keeps working.
/// The resource URL registered here is purely an authorization-time alias the AS recognizes —
/// it is not propagated to the access token's <c>aud</c> claim.
/// </para>
///
/// <para>
/// Idempotent: on second boot it's a no-op. Mirrors the retry/backoff pattern from
/// <see cref="ClientAllowListSeeder"/> so a Railway cold start with a momentarily
/// unreachable database doesn't leave the scope store empty.
/// </para>
/// </summary>
public sealed class ScopeAllowListSeeder(
    IServiceProvider services,
    McpResourceConfiguration resources,
    ILogger<ScopeAllowListSeeder> logger) : BackgroundService
{
    // Same retry shape as ClientAllowListSeeder — see that class's rationale.
    private const int MaxAttempts = 10;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The scopes the AS advertises and accepts. Mirrors the
    /// <c>options.RegisterScopes(...)</c> call in <c>Program.cs</c> — both are required.
    /// <c>RegisterScopes</c> is OpenIddict's in-memory permitted-scope list (it gates which
    /// scopes the request validators accept and what shows up in
    /// <c>scopes_supported</c>); the persistent records this seeder writes carry the
    /// resource bindings the resource validator reads.
    /// </summary>
    private static readonly string[] CivitiScopes =
    [
        "civiti.read",
        "civiti.write",
        "civiti.admin.read",
        "civiti.admin.write"
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Diagnostic entry log so we can confirm the seeder is being invoked and see what
        // resource list it received. The "Updated" path below was previously LogDebug,
        // which made first-deploy verification impossible — we couldn't tell from logs
        // whether the seeder hit the Create or Update branch on each scope.
        logger.LogInformation(
            "ScopeAllowListSeeder starting with {ResourceCount} resource(s): [{Resources}]",
            resources.Resources.Count, string.Join(',', resources.Resources));

        if (resources.Resources.Count == 0)
        {
            // No resources configured — surface this loudly so operators see the
            // misconfiguration in the first log scrape rather than only after users start
            // reporting "invalid_target" from Claude Desktop. Falling through (rather than
            // returning early) is deliberate: a previous deployment may have left URLs on
            // existing scope records, and re-stamping with an empty Resources collection
            // is what actually makes the warning's claim true. Returning early would leave
            // stale bindings active and the warning would be a lie.
            logger.LogWarning(
                "ScopeAllowListSeeder: no MCP resource URLs configured (Auth:McpResources / MCP_RESOURCES). " +
                "RFC 8707 resource indicators will be rejected with invalid_target. " +
                "Existing scope records will have their Resources collection cleared.");
        }

        var backoff = InitialBackoff;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

                foreach (var scopeName in CivitiScopes)
                {
                    var descriptor = BuildDescriptor(scopeName, resources.Resources);
                    var existing = await manager.FindByNameAsync(scopeName, stoppingToken);
                    if (existing is null)
                    {
                        await manager.CreateAsync(descriptor, stoppingToken);
                        logger.LogInformation(
                            "Seeded OpenIddict scope {Scope} with {ResourceCount} resource(s)",
                            scopeName, resources.Resources.Count);
                    }
                    else
                    {
                        await manager.UpdateAsync(existing, descriptor, stoppingToken);
                        logger.LogInformation(
                            "Updated OpenIddict scope {Scope} with {ResourceCount} resource(s)",
                            scopeName, resources.Resources.Count);
                    }
                }
                logger.LogInformation(
                    "ScopeAllowListSeeder complete: {Count} scope(s) processed.",
                    CivitiScopes.Length);
                return;
            }
            catch (OperationCanceledException)
            {
                return; // Host shutting down.
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex,
                    "Scope allow-list seed attempt {Attempt}/{Max} failed; retrying in {Backoff}.",
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
            // Final attempt exception unwinds past the loop; BackgroundService surfaces it
            // and the pod stays up with /authorize rejecting any request that carries a
            // resource indicator until operator intervention.
        }
    }

    /// <summary>
    /// Builds the descriptor written to the scope manager. <c>Resources</c> is the only
    /// non-trivial field — display name / description are intentionally left null so the
    /// consent UI continues to read its labels from <c>Pages/Consent.cshtml.cs</c>'s
    /// <c>ScopeDescriptor.For</c> switch (which is the source of truth for user-facing
    /// strings).
    /// </summary>
    public static OpenIddictScopeDescriptor BuildDescriptor(string name, IReadOnlyCollection<string> resources)
    {
        var descriptor = new OpenIddictScopeDescriptor { Name = name };
        foreach (var resource in resources)
        {
            descriptor.Resources.Add(resource);
        }
        return descriptor;
    }
}

/// <summary>
/// Resolved at startup from <c>Auth:McpResources</c> configuration or the
/// <c>MCP_RESOURCES</c> env var (comma-separated). Validated to contain only absolute HTTPS
/// URIs; the validation prevents misconfigured entries from quietly getting written to the
/// scope manager and silently shifting the resource validator's accept set.
///
/// Registered as a singleton so <see cref="ScopeAllowListSeeder"/> can read it at startup.
/// </summary>
public sealed class McpResourceConfiguration
{
    public IReadOnlyCollection<string> Resources { get; }

    public McpResourceConfiguration(IEnumerable<string> resources)
    {
        var validated = new List<string>();
        foreach (var raw in resources)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    $"Auth:McpResources entry '{raw}' is not a valid absolute HTTPS URI. " +
                    "Expected e.g. 'https://civiti-mcp-development.up.railway.app/mcp'.");
            }
            validated.Add(trimmed);
        }
        Resources = validated;
    }

    /// <summary>
    /// Reads the resource URL list from configuration (<c>Auth:McpResources</c> as a string
    /// array) or the <c>MCP_RESOURCES</c> env var (comma-separated). The env var wins when
    /// both are set, mirroring the Supabase config pattern in <c>Program.cs</c>.
    /// </summary>
    public static McpResourceConfiguration FromConfiguration(IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable("MCP_RESOURCES");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return new McpResourceConfiguration(
                fromEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        var fromConfig = configuration.GetSection("Auth:McpResources").Get<string[]>() ?? Array.Empty<string>();
        return new McpResourceConfiguration(fromConfig);
    }
}
