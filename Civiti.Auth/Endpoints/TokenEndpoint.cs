using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// /token entry point for the OAuth code-exchange grant. v1b.1 implements
/// <c>grant_type=authorization_code</c> only; <c>refresh_token</c> rotation with Supabase
/// Admin API re-validation lands in v1b.2.
///
/// OpenIddict's middleware has already validated the code (consumed it from
/// <c>OpenIddictTokens</c>), PKCE verifier, redirect_uri, and client credentials by the time
/// this handler runs. We recover the principal embedded in the code, re-attach claim
/// destinations, and SignIn the OpenIddict scheme so the middleware emits the access + refresh
/// tokens. The McpSession audit row is written by
/// <see cref="McpSessionWriteHandler"/> from inside the OpenIddict signin pipeline so it
/// shares a code path with token issuance instead of committing speculatively from here.
/// </summary>
internal static class TokenEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
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

        if (!oidcRequest.IsAuthorizationCodeGrantType())
        {
            // Refresh-token grant flows reach this handler because AllowRefreshTokenFlow() is on
            // in the OpenIddict server config (so v1b.1 issues refresh tokens for v1b.2 to
            // consume). Until v1b.2 wires Supabase Admin API re-validation, we surface the
            // RFC 6749 §5.2 unsupported_grant_type error via OpenIddict's properties pipeline so
            // OAuth clients get the canonical {"error":"unsupported_grant_type"} body and trigger
            // a fresh /authorize round-trip rather than failing on an unknown ProblemDetails shape.
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.UnsupportedGrantType,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                    "Refresh-token rotation lands in v1b.2; re-run /authorize to obtain a fresh access token."
            });
            return Results.Challenge(properties,
                authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });
        }

        var info = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!info.Succeeded || info.Principal is null)
        {
            logger.LogWarning("/token: failed to recover principal from authorization code");
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

        // Claim destinations don't survive the encrypted authorization code round-trip; re-attach
        // before SignIn so OpenIddict knows which claims to embed in the access vs identity token.
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim));
        }

        logger.LogInformation("/token: code-grant exchange for sub {Sub}, client {ClientId}",
            supabaseUserId, oidcRequest.ClientId);

        return Results.SignIn(principal, properties: null,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
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
