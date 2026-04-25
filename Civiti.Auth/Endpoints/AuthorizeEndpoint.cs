using System.Security.Claims;
using Civiti.Auth.Authentication;
using Civiti.Infrastructure.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// /authorize entry point. Three paths:
///
/// 1. <b>No cookie session</b> — redirect to <c>/Login?returnUrl=/authorize?...</c>. The
///    Login Razor page handles email+password and the "Sign in with Google" button (which
///    bounces through Supabase's PKCE flow and lands on <c>/supabase-callback</c>).
/// 2. <b>Cookie session, but no consent record covering the requested scopes</b> — redirect
///    to <c>/Consent?returnUrl=/authorize?...</c>. The Consent page upserts an
///    <c>McpUserClientPreference</c> row on approve and redirects back here, where this path
///    is then satisfied.
/// 3. <b>Cookie session + consent (or scopes don't need consent)</b> — apply
///    <see cref="AdminScopeFilter"/>, build the OpenIddict <see cref="ClaimsPrincipal"/>, set
///    claim destinations, and SignIn so the OpenIddict middleware emits the auth-code redirect.
/// </summary>
public static class AuthorizeEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        AdminScopeFilter adminScopeFilter,
        CivitiDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Civiti.Auth.Endpoints.Authorize");

        var oidcRequest = httpContext.GetOpenIddictServerRequest();
        if (oidcRequest is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid OAuth request",
                detail: "OpenIddict request context is unavailable.");
        }

        var cookieAuth = await httpContext.AuthenticateAsync(AuthEndpointConstants.CookieScheme);
        if (!cookieAuth.Succeeded || cookieAuth.Principal is null)
        {
            // Path 1 — no session, send the user to /Login. Login.cshtml handles both
            // email+password and the Google PKCE redirect.
            var returnUrl = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
            logger.LogInformation("/authorize: no cookie session, redirecting to /Login");
            return Results.Redirect($"/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var supabaseUserId = cookieAuth.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            logger.LogWarning("/authorize: cookie session missing NameIdentifier claim");
            return Results.Forbid(
                authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });
        }

        var role = cookieAuth.Principal.FindFirst(ClaimTypes.Role)?.Value;
        var clientId = oidcRequest.ClientId ?? string.Empty;
        var requestedScopes = oidcRequest.GetScopes();

        // §6 admin gating runs early so the consent screen + final SignIn agree on the same
        // scope set; the Consent page re-runs the filter on POST to defeat any client-side
        // tampering of the form.
        var filter = await adminScopeFilter.FilterAsync(clientId, role, requestedScopes, cancellationToken);
        var allowedScopes = filter.Allowed.ToList();

        var hasConsent = await HasConsentForScopesAsync(dbContext, supabaseUserId, clientId, allowedScopes, cancellationToken);
        if (!hasConsent)
        {
            // Path 2 — drive the user through the consent screen, which will bounce back here
            // with the McpUserClientPreference row in place. We re-render /authorize on return
            // so the OpenIddict request payload (PKCE challenge, redirect_uri, …) is intact.
            var returnUrl = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
            logger.LogInformation(
                "/authorize: cookie session present but no consent covering scopes [{Scopes}] for client {ClientId}; redirecting to /Consent",
                string.Join(',', allowedScopes), clientId);
            return Results.Redirect($"/Consent?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        // Path 3 — issue the authorization code.
        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, supabaseUserId));
        if (!string.IsNullOrEmpty(role))
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));
        }

        foreach (var claim in identity.Claims)
        {
            claim.SetDestinations(GetDestinations(claim));
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(allowedScopes);
        principal.SetResources(AuthEndpointConstants.ResourceServer);

        logger.LogInformation(
            "/authorize: issuing authorization code for sub {Sub}, scopes {Scopes}",
            supabaseUserId, string.Join(',', allowedScopes));

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<bool> HasConsentForScopesAsync(
        CivitiDbContext dbContext,
        string supabaseUserId,
        string clientId,
        IReadOnlyCollection<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        if (requestedScopes.Count == 0)
        {
            // No app-scopes requested (e.g. only `openid`). Always show consent on first run so
            // the user explicitly opts in; cheap and avoids surprising silent grants.
            // The check below would always return true otherwise.
        }

        var preference = await dbContext.McpUserClientPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.SupabaseUserId == supabaseUserId && p.ClientId == clientId,
                cancellationToken);

        if (preference is null) return false;

        var granted = preference.ScopesGranted.ToHashSet(StringComparer.Ordinal);
        return requestedScopes.All(granted.Contains);
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
