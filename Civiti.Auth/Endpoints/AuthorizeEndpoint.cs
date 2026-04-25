using System.Security.Claims;
using Civiti.Auth.Authentication;
using Civiti.Infrastructure.Configuration;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// /authorize entry point. Two paths:
///
/// 1. <b>No cookie session</b> — generate a PKCE pair, persist (verifier + return-URL) as
///    encrypted state, redirect the user to Supabase's OAuth provider. The flow resumes at
///    /supabase-callback.
/// 2. <b>Cookie session present</b> — convert the cookie principal into an OpenIddict
///    principal (sub + role + scopes + resource), set claim destinations, and SignIn the
///    OpenIddict server scheme. OpenIddict's middleware then emits the authorization-code
///    redirect back to the MCP client.
///
/// v1b.1 deliberately ships OAuth-only login (Google, configured in Supabase). Email
/// magic-link / password / provider selection land in v1b.2 with the consent Razor page —
/// they all need user-facing UI which is out of scope here.
/// </summary>
internal static class AuthorizeEndpoint
{
    private const string DefaultProvider = "google";
    private const string ProviderEnvVar = "SUPABASE_LOGIN_PROVIDER";

    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        SupabasePkceStateProtector stateProtector,
        SupabaseConfiguration supabaseConfig,
        ILoggerFactory loggerFactory)
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
        if (cookieAuth.Succeeded && cookieAuth.Principal is not null)
        {
            return IssueAuthorizationCode(oidcRequest, cookieAuth.Principal, logger);
        }

        var (verifier, challenge) = PkceCodes.Generate();
        var returnUrl = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
        var state = new SupabasePkceState(verifier, returnUrl, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var protectedState = stateProtector.Protect(state);

        var callbackUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{AuthEndpointConstants.SupabaseCallbackPath}";
        var provider = Environment.GetEnvironmentVariable(ProviderEnvVar) ?? DefaultProvider;

        var supabaseAuthorizeUrl =
            $"{supabaseConfig.Url.TrimEnd('/')}/auth/v1/authorize" +
            $"?provider={Uri.EscapeDataString(provider)}" +
            $"&redirect_to={Uri.EscapeDataString(callbackUrl)}" +
            $"&code_challenge={challenge}" +
            $"&code_challenge_method=S256" +
            $"&state={Uri.EscapeDataString(protectedState)}";

        logger.LogInformation("/authorize: redirecting to Supabase ({Provider}) for client {ClientId}",
            provider, oidcRequest.ClientId);
        return Results.Redirect(supabaseAuthorizeUrl);
    }

    private static IResult IssueAuthorizationCode(
        OpenIddictRequest oidcRequest,
        ClaimsPrincipal cookiePrincipal,
        ILogger logger)
    {
        var supabaseUserId = cookiePrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            logger.LogWarning("/authorize: cookie session missing NameIdentifier claim");
            return Results.Forbid(
                authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme });
        }

        var role = cookiePrincipal.FindFirst(ClaimTypes.Role)?.Value;

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
        principal.SetScopes(oidcRequest.GetScopes());
        principal.SetResources(AuthEndpointConstants.ResourceServer);

        logger.LogInformation("/authorize: issuing authorization code for sub {Sub}, scopes {Scopes}",
            supabaseUserId, string.Join(',', oidcRequest.GetScopes()));

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Claim destinations follow the OpenIddict convention: subject + role flow into both
    // the access token (so Civiti.Mcp can authorize calls) and the identity token (where
    // applicable). v1b.2 will add scope-aware destinations once we introduce id_token-only
    // claims like email + display_name.
    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        OpenIddictConstants.Claims.Subject =>
            new[] { OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken },
        OpenIddictConstants.Claims.Role =>
            new[] { OpenIddictConstants.Destinations.AccessToken },
        _ => new[] { OpenIddictConstants.Destinations.AccessToken }
    };
}
