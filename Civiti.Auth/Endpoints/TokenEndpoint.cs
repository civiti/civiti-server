using System.Security.Claims;
using Civiti.Application.Services;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// /token entry point. Two grants:
///
/// 1. <c>grant_type=authorization_code</c> — OpenIddict has already validated the code, PKCE
///    verifier, redirect_uri, and client credentials. We recover the principal embedded in the
///    code, re-attach claim destinations, and SignIn so the middleware emits access + refresh
///    tokens. McpSessionWriteHandler tracks the row.
/// 2. <c>grant_type=refresh_token</c> — OpenIddict has validated the refresh token (and
///    rotated/consumed it). Before re-issuing, we re-validate the upstream Supabase user via
///    the Admin API: missing user / disabled (banned) / role-changed all force a deny so the
///    refresh fails and the user has to log in fresh. Per auth-design.md §4 every refresh
///    re-validates against Supabase — this is what catches role demotions and account
///    deletions before access tokens stop being issued.
/// </summary>
public static class TokenEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        ISupabaseAdminClient supabaseAdminClient,
        AdminScopeFilter adminScopeFilter,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Civiti.Auth.Endpoints.Token");

        var oidcRequest = httpContext.GetOpenIddictServerRequest();
        if (oidcRequest is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid OAuth request",
                detail: "OpenIddict request context is unavailable.");
        }

        if (!oidcRequest.IsAuthorizationCodeGrantType() && !oidcRequest.IsRefreshTokenGrantType())
        {
            return ChallengeWithError(OpenIddictConstants.Errors.UnsupportedGrantType,
                "Only authorization_code and refresh_token grants are supported.");
        }

        var info = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!info.Succeeded || info.Principal is null)
        {
            logger.LogWarning("/token: failed to recover principal");
            return Results.Forbid(
                authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });
        }

        var principal = info.Principal;
        var supabaseUserId = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            logger.LogWarning("/token: principal missing subject claim");
            return Results.Forbid(
                authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });
        }

        if (oidcRequest.IsRefreshTokenGrantType())
        {
            SupabaseUserSnapshot? snapshot;
            try
            {
                snapshot = await supabaseAdminClient.GetUserAsync(supabaseUserId, cancellationToken);
            }
            catch (SupabaseTransientException ex)
            {
                // Fail closed: a Supabase outage during refresh shouldn't let a possibly-revoked
                // user keep their session. The user re-authenticates on next attempt; the cost
                // is one extra interactive login, the saved cost is potentially extending an
                // admin-scoped session past a legitimate demotion.
                logger.LogWarning(ex,
                    "/token refresh: transient Supabase error for sub {Sub} — denying refresh",
                    supabaseUserId);
                return ChallengeWithError(OpenIddictConstants.Errors.InvalidGrant,
                    "Could not revalidate the upstream user. Please sign in again.");
            }
            if (snapshot is null)
            {
                logger.LogWarning(
                    "/token refresh: Supabase user {Sub} not found — denying refresh, session must re-authenticate",
                    supabaseUserId);
                return ChallengeWithError(OpenIddictConstants.Errors.InvalidGrant,
                    "The upstream user no longer exists.");
            }
            if (snapshot.BannedUntilUtc is { } bannedUntil && bannedUntil > DateTime.UtcNow)
            {
                logger.LogWarning(
                    "/token refresh: Supabase user {Sub} is banned until {Until} — denying refresh",
                    supabaseUserId, bannedUntil);
                return ChallengeWithError(OpenIddictConstants.Errors.InvalidGrant,
                    "The upstream user is currently disabled.");
            }

            // Reflect the latest Supabase role onto the rotated refresh token's principal so a
            // demotion (admin → citizen) takes effect immediately on this rotation.
            UpdateRoleClaim(principal, snapshot.Role);

            // Also re-run AdminScopeFilter against the principal's scopes. Without this, a user
            // demoted between refreshes would still get admin-scoped access tokens for the
            // remaining 15-min TTL — the role claim says "citizen" but civiti.admin.* is in the
            // scope set, and any consumer that gates on scope (Civiti.Mcp will) would treat the
            // token as admin until expiry. The role-revalidation sweep revokes the *refresh*
            // token within 5 min, which stops future refreshes; this stops the new access token
            // issued by *this* refresh from carrying admin authority.
            //
            // OpenIddict always populates ClientId on a refresh-token grant; if it's somehow
            // missing we'd silently strip every admin scope (FilterAsync('') treats no app as
            // not-allowed) and the warning log would read clientId="", making a parameter-bug
            // look like a role demotion. Bail with an explicit error instead.
            var clientId = oidcRequest.ClientId;
            if (string.IsNullOrEmpty(clientId))
            {
                logger.LogWarning("/token refresh: missing client_id on refresh request for sub {Sub}", supabaseUserId);
                return ChallengeWithError(OpenIddictConstants.Errors.InvalidClient,
                    "client_id is required on refresh requests.");
            }
            var currentScopes = principal.GetScopes();
            var filter = await adminScopeFilter.FilterAsync(
                clientId, snapshot.Role, currentScopes, cancellationToken);
            if (filter.Stripped.Count > 0)
            {
                principal.SetScopes(filter.Allowed);
                logger.LogWarning(
                    "/token refresh: stripped admin scopes [{Stripped}] from sub {Sub} (role now {Role})",
                    string.Join(',', filter.Stripped), supabaseUserId, snapshot.Role ?? "(none)");
            }
        }

        // Claim destinations don't survive the encrypted code/refresh round-trip; re-attach
        // before SignIn so OpenIddict knows which claims to embed in the access vs identity token.
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim));
        }

        logger.LogInformation("/token: {Grant} for sub {Sub}, client {ClientId}",
            oidcRequest.GrantType, supabaseUserId, oidcRequest.ClientId);

        return Results.SignIn(principal, properties: null,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult ChallengeWithError(string error, string description)
    {
        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        });
        return Results.Challenge(properties,
            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });
    }

    private static void UpdateRoleClaim(ClaimsPrincipal principal, string? role)
    {
        if (principal.Identity is not ClaimsIdentity identity) return;

        var existing = identity.FindFirst(OpenIddictConstants.Claims.Role);
        if (existing is not null) identity.RemoveClaim(existing);

        if (!string.IsNullOrEmpty(role))
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));
        }
    }

    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        OpenIddictConstants.Claims.Subject =>
            new[] { OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken },
        OpenIddictConstants.Claims.Role =>
            new[] { OpenIddictConstants.Destinations.AccessToken },
        _ => new[] { OpenIddictConstants.Destinations.AccessToken }
    };
}
