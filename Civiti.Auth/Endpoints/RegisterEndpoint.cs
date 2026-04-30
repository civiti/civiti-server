using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// RFC 7591 OAuth Dynamic Client Registration. Required by every modern remote-MCP client
/// (Claude Desktop, Claude Code, Cursor, …) — the streamlined "Add custom connector" UX
/// doesn't have a "paste a pre-shared client_id" path, so the AS must let the client
/// register itself. Without this endpoint Claude Desktop's Connect flow hangs forever
/// after the discovery hop.
///
/// Guardrails per <c>auth-design.md §6</c>:
/// <list type="bullet">
/// <item>Loopback redirect URIs only — RFC 8252 §8.3 (<c>http://127.0.0.1:*/...</c> or
/// <c>http://[::1]:*/...</c>). Reuses the same matcher the authorize/token validators use.</item>
/// <item>Scope ceiling — <c>civiti.admin.*</c> is silently stripped from the requested
/// scopes. DCR-registered clients can never hold admin scopes regardless of what their
/// users grant on the consent screen. Pre-allow-listed clients (claude-desktop,
/// claude-code) keep their <c>allowsAdminScopes</c> grant — those are seeded in
/// <see cref="Civiti.Auth.Startup.ClientAllowListSeeder"/>.</item>
/// <item>Per-IP rate limit — 20 registrations / source IP / 24h, applied via the
/// <c>dcr-register</c> rate-limiter policy in <c>Program.cs</c>.</item>
/// <item>PKCE required — same requirement the seeded clients carry.</item>
/// </list>
///
/// The endpoint is anonymous: the validation gates above are the safety boundary. CORS
/// is inherited from the global <c>Claude</c> policy mounted by
/// <see cref="Civiti.Auth.Startup.CorsStartupFilter"/>.
/// </summary>
public static class RegisterEndpoint
{
    /// <summary>Rate-limit policy name; see Program.cs registration.</summary>
    public const string RateLimitPolicy = "dcr-register";

    /// <summary>Path the endpoint is mounted at; mirrors the value advertised in the discovery doc.</summary>
    public const string Path = "/register";

    // Scope ceiling per auth-design.md §6: DCR-registered clients can only request the
    // citizen scopes; civiti.admin.* is reserved for pre-allow-listed entries that carry
    // the allowsAdminScopes flag (claude-desktop, claude-code today). Mirrors the
    // CivitiReadScope / CivitiWriteScope literals in Civiti.Mcp.Authorization.McpCitizenContext.
    private static readonly HashSet<string> AllowedDcrScopes = new(StringComparer.Ordinal)
    {
        "civiti.read",
        "civiti.write"
    };

    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder routes)
    {
        return routes.MapPost(Path, HandleAsync)
            .RequireRateLimiting(RateLimitPolicy)
            .ExcludeFromDescription();
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] RegisterClientRequest? request,
        IOpenIddictApplicationManager applicationManager,
        ILogger<RegisterClientRequest> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return RegistrationError("invalid_client_metadata", "Request body is required.");
        }

        var redirectUris = request.RedirectUris ?? new List<string>();
        if (redirectUris.Count == 0)
        {
            return RegistrationError("invalid_redirect_uri", "redirect_uris is required and must contain at least one URI.");
        }
        foreach (var uri in redirectUris)
        {
            if (string.IsNullOrWhiteSpace(uri) || !LoopbackRedirectUriMatcher.IsLoopback(uri))
            {
                // Per the auth-design.md §6 guardrail: only loopback URIs are accepted via
                // DCR. Allow-listed entries keep their first-party redirect URIs (HTTPS for
                // claude-ai, custom URL scheme for cursor) — those are seeded explicitly,
                // not registered.
                return RegistrationError(
                    "invalid_redirect_uri",
                    $"redirect_uri '{uri}' is not allowed. Dynamically-registered clients must use a loopback URI per RFC 8252 §8.3 (http://127.0.0.1:*/... or http://[::1]:*/...).");
            }
        }

        // Scope ceiling: silently strip civiti.admin.*. Echo back only the granted subset
        // in the response so the client knows exactly what's available. If the client asked
        // for nothing, default to read+write so the typical Claude Desktop experience works
        // out of the box (the consent screen is the user's per-scope opt-out).
        var requestedScopes = ParseScope(request.Scope);
        var grantedScopes = (requestedScopes.Count == 0 ? AllowedDcrScopes : requestedScopes)
            .Where(AllowedDcrScopes.Contains)
            .ToHashSet(StringComparer.Ordinal);
        if (grantedScopes.Count == 0)
        {
            return RegistrationError(
                "invalid_client_metadata",
                $"None of the requested scopes are grantable via DCR. Allowed: {string.Join(", ", AllowedDcrScopes)}.");
        }

        var clientId = Guid.NewGuid().ToString("N");
        var displayName = string.IsNullOrWhiteSpace(request.ClientName)
            ? $"Dynamically registered client ({clientId[..8]})"
            : request.ClientName.Trim();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            // Public client: no secret, native app per RFC 8252.
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ApplicationType = OpenIddictConstants.ApplicationTypes.Native,
            // Explicit consent required every time — there's no Trust-on-First-Use for DCR
            // clients since the user has no out-of-band reason to trust them.
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit
        };
        foreach (var uri in redirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }

        // Endpoints + grant types + response types — same shape the seeder uses for the
        // allow-listed clients minus the admin-only flows (which DCR clients can never hit).
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);

        foreach (var scope in grantedScopes)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
        }

        // PKCE: every native loopback client MUST use it. OpenIddict treats this as a
        // Requirement (vs Permission) — the auth code flow rejects requests without a
        // code_challenge for clients carrying this requirement.
        descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

        try
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DCR: failed to persist new client {ClientId} from {RemoteIp}",
                clientId, httpContext.Connection.RemoteIpAddress);
            return RegistrationError("server_error", "Could not persist the new client.");
        }

        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        logger.LogInformation(
            "DCR: registered client {ClientId} ('{DisplayName}') from {RemoteIp} with scopes [{Scopes}] and {RedirectUriCount} redirect URI(s)",
            clientId, displayName, httpContext.Connection.RemoteIpAddress, string.Join(',', grantedScopes), redirectUris.Count);

        return Results.Json(
            new RegisterClientResponse(
                ClientId: clientId,
                ClientIdIssuedAt: issuedAt,
                ClientName: displayName,
                RedirectUris: redirectUris.ToArray(),
                GrantTypes: ["authorization_code", "refresh_token"],
                ResponseTypes: ["code"],
                TokenEndpointAuthMethod: "none",
                ApplicationType: "native",
                Scope: string.Join(' ', grantedScopes.OrderBy(s => s, StringComparer.Ordinal))),
            statusCode: StatusCodes.Status201Created);
    }

    private static HashSet<string> ParseScope(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        return new HashSet<string>(
            raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);
    }

    private static IResult RegistrationError(string error, string description) =>
        Results.Json(
            new { error, error_description = description },
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>
/// RFC 7591 §2 client metadata fields. Only the bits we actually care about; everything
/// else (e.g. <c>jwks</c>, <c>contacts</c>, <c>logo_uri</c>) is silently ignored at this
/// stage — we can add fields when a real client demands them.
/// </summary>
public sealed class RegisterClientRequest
{
    [JsonPropertyName("redirect_uris")]
    public List<string>? RedirectUris { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

/// <summary>
/// RFC 7591 §3.2.1 client registration response. <c>token_endpoint_auth_method = "none"</c>
/// flags this as a public client (no secret); <c>application_type = "native"</c> matches the
/// loopback-redirect rule.
/// </summary>
public sealed record RegisterClientResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] string[] RedirectUris,
    [property: JsonPropertyName("grant_types")] string[] GrantTypes,
    [property: JsonPropertyName("response_types")] string[] ResponseTypes,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
    [property: JsonPropertyName("application_type")] string ApplicationType,
    [property: JsonPropertyName("scope")] string Scope);
