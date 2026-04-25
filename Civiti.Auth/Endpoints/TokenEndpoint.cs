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
            var snapshot = await supabaseAdminClient.GetUserAsync(supabaseUserId, cancellationToken);
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
            // demotion (admin → citizen) takes effect immediately. Admin scopes that the user
            // no longer qualifies for will be filtered by AdminScopeFilter on the next /authorize
            // — for now, a same-set rotation with the updated role is sufficient. Wholesale
            // scope re-evaluation on refresh lands once we have a UserProfile.McpAdminAccessEnabled
            // sweep (auth-design.md §5).
            UpdateRoleClaim(principal, snapshot.Role);
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
