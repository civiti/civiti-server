using System.Security.Claims;
using Civiti.Domain.Entities;
using Civiti.Infrastructure.Data;
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
/// this handler runs. We just recover the principal embedded in the code, re-attach claim
/// destinations, write the McpSession audit row, and SignIn the OpenIddict scheme so the
/// middleware emits the access + refresh tokens.
/// </summary>
internal static class TokenEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        CivitiDbContext dbContext,
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

        if (!oidcRequest.IsAuthorizationCodeGrantType())
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsupported grant type",
                detail: "v1b.1 supports grant_type=authorization_code only. Refresh-token rotation lands in v1b.2.");
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

        // McpSession.OpenIddictTokenId stays null in v1b.1. The session row is the source of
        // truth for the "Connected AI Assistants" UI; the link to OpenIddict's refresh-token row
        // gets repointed atomically on the first refresh in v1b.2 (auth-design.md §5).
        // Pre-creating the row here means a SignIn failure later in OpenIddict's pipeline could
        // leave an orphan; v1b.2's IOpenIddictServerHandler<ProcessSignInContext> hook moves the
        // write to the post-issuance event so it stays in lockstep with token state.
        var session = new McpSession
        {
            Id = Guid.NewGuid(),
            ClientId = oidcRequest.ClientId ?? string.Empty,
            SupabaseUserId = supabaseUserId,
            ScopesGranted = principal.GetScopes().ToList(),
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        dbContext.McpSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("/token: minted tokens for sub {Sub}, client {ClientId}, session {SessionId}",
            supabaseUserId, oidcRequest.ClientId, session.Id);

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
