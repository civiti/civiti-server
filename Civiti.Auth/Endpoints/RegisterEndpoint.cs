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
/// <item>Loopback redirect URIs (RFC 8252 §8.3 — <c>http://127.0.0.1:*/...</c> or
/// <c>http://[::1]:*/...</c>) or an explicit allowlist of cloud-relay callbacks
/// (<c>https://claude.ai/api/mcp/auth_callback</c> today). The allowlist exists because
/// Claude Desktop's "Add custom connector" doesn't bind a loopback port — it relays the
/// auth code through Anthropic's infrastructure. See
/// <see cref="LoopbackRedirectUriMatcher.AllowlistedDcrRedirectUris"/>.</item>
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

    // Citizen-scope ceiling per auth-design.md §6: DCR-registered clients can only request
    // these resource scopes; civiti.admin.* is reserved for pre-allow-listed entries that
    // carry the allowsAdminScopes flag (claude-desktop, claude-code today). Mirrors the
    // CivitiReadScope / CivitiWriteScope literals in Civiti.Mcp.Authorization.McpCitizenContext.
    // offline_access is granted unconditionally on top of this ceiling — see grantedScopes
    // construction below.
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

    /// <summary>
    /// Length cap on the client-supplied <c>client_name</c> before persistence. OpenIddict's
    /// EF column for <c>DisplayName</c> doesn't enforce a tight limit, but a multi-MB name
    /// would either silently truncate (data loss) or trip a DB error that falls through to a
    /// generic "server_error" 400. 200 chars matches the default OpenIddict scaffolding and
    /// is generous enough for any honest client identifier.
    /// </summary>
    private const int MaxClientNameLength = 200;

    /// <summary>
    /// Cap on the number of redirect URIs accepted in a single registration. Real native
    /// clients have one (a loopback callback) and at most a handful (one per OS / port
    /// arrangement) — 10 is comfortable headroom. Without this cap the 20-req/IP/day rate
    /// limit still leaves a path to write hundreds of thousands of OpenIddict
    /// redirect-URI rows / day from a single source, which would slow every subsequent
    /// <c>GetRedirectUrisAsync</c> on that application.
    /// </summary>
    private const int MaxRedirectUrisPerRegistration = 10;

    private static async Task<IResult> HandleAsync(
        [FromBody] RegisterClientRequest? request,
        IOpenIddictApplicationManager applicationManager,
        ILogger<DynamicClientRegistration> logger,
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
        if (redirectUris.Count > MaxRedirectUrisPerRegistration)
        {
            return RegistrationError(
                "invalid_redirect_uri",
                $"redirect_uris exceeds the {MaxRedirectUrisPerRegistration}-entry limit per registration.");
        }
        foreach (var uri in redirectUris)
        {
            if (string.IsNullOrWhiteSpace(uri) || !LoopbackRedirectUriMatcher.IsAcceptableDcrRedirectUri(uri))
            {
                // Per the auth-design.md §6 guardrail: DCR accepts loopback URIs (native apps
                // that bind a local port — Claude Code, Cursor) plus a tiny allowlist of
                // documented cloud-relay callbacks (Claude Desktop's claude.ai/api/mcp
                // /auth_callback). Pre-seeded clients with their own first-party redirect
                // URIs (e.g. custom URL schemes) are configured via ClientAllowListSeeder,
                // not registered through this endpoint.
                return RegistrationError(
                    "invalid_redirect_uri",
                    $"redirect_uri '{uri}' is not allowed. Dynamically-registered clients must use a loopback URI per RFC 8252 §8.3 (http://127.0.0.1:*/... or http://[::1]:*/...) or one of the allowlisted cloud-relay callbacks: {string.Join(", ", LoopbackRedirectUriMatcher.AllowlistedDcrRedirectUris)}.");
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
                $"At least one citizen scope is required. Allowed citizen scopes: {string.Join(", ", AllowedDcrScopes)}. The offline_access scope is granted automatically on top of citizen scopes and cannot be requested on its own.");
        }

        // offline_access is always added on top of the citizen-scope ceiling. Without it the
        // registration response's advertised grant_types: ["authorization_code", "refresh_token"]
        // is a lie — OpenIddict only mints a refresh token when the /authorize request
        // includes scope=offline_access, and a client that doesn't see offline_access in the
        // returned scope set has no way to know to request it. Adding it here also lets the
        // descriptor-permission loop below grant Permissions.Scopes.OfflineAccess, which
        // keeps the per-client scope gate honest if IgnoreScopePermissions() in Program.cs
        // is ever removed.
        grantedScopes.Add(OpenIddictConstants.Scopes.OfflineAccess);

        var clientId = Guid.NewGuid().ToString("N");
        var trimmedName = request.ClientName?.Trim();
        if (!string.IsNullOrEmpty(trimmedName) && trimmedName.Length > MaxClientNameLength)
        {
            return RegistrationError(
                "invalid_client_metadata",
                $"client_name exceeds the {MaxClientNameLength}-character limit.");
        }
        var displayName = string.IsNullOrEmpty(trimmedName)
            ? $"Dynamically registered client ({clientId[..8]})"
            : trimmedName;

        // RFC 7591 §2: "native" covers installed apps with loopback / custom URI schemes;
        // "web" covers HTTPS callback registrations. A registration with only HTTPS URIs
        // (e.g. Claude Desktop's claude.ai relay) is semantically a web client even though
        // we issue it as a public client (no secret, PKCE-required). A loopback URI in the
        // set forces "native" because the caller has a local listener.
        var applicationType = LoopbackRedirectUriMatcher.DeriveApplicationType(redirectUris);

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            // Public client: no secret. PKCE is required below, which is what makes a
            // public client safe regardless of native/web application_type.
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ApplicationType = applicationType,
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
        catch (OperationCanceledException)
        {
            // Client disconnected or the request timed out — not a server-side defect, so
            // bubble up rather than logging an error and returning a misleading 400. The
            // ASP.NET Core pipeline turns this into a 499/clean abort with no log noise.
            throw;
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
                ApplicationType: applicationType,
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
/// Logger-category marker. <see cref="RegisterEndpoint"/> is static and so can't be used as a
/// generic type argument (CS0718), but ASP.NET Core's logger DI keys on the generic-type
/// argument — so we route <c>ILogger&lt;DynamicClientRegistration&gt;</c> instead, which gives
/// every log line emitted from the DCR endpoint the category
/// <c>Civiti.Auth.Endpoints.DynamicClientRegistration</c> (instead of, say, the request DTO's
/// name).
/// </summary>
internal sealed class DynamicClientRegistration { }

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
/// flags this as a public client (no secret). <c>application_type</c> is derived per
/// registration via <see cref="LoopbackRedirectUriMatcher.DeriveApplicationType"/>:
/// <c>"native"</c> when any redirect URI is loopback, <c>"web"</c> when all are HTTPS.
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
