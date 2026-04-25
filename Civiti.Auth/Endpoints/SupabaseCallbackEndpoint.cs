using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Civiti.Auth.Authentication;
using Civiti.Infrastructure.Configuration;
using Civiti.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// Receives Supabase's PKCE callback (<c>?code=...&amp;state=...</c>), exchanges the code for
/// a short-lived Supabase access JWT, validates it against Supabase's JWKS, looks up the
/// matching <c>UserProfile</c> row, sets a Civiti.Auth session cookie, and redirects back to
/// the original /authorize URL so OpenIddict can finish minting the authorization code.
///
/// The Supabase JWT itself is discarded after extracting <c>sub</c> + <c>role</c> + <c>email</c>
/// (auth-design.md §4: "immediately discards the Supabase JWT" — Civiti.Auth is the identity
/// edge once its own tokens are issued).
/// </summary>
internal static class SupabaseCallbackEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        SupabasePkceStateProtector stateProtector,
        SupabaseTokenValidator tokenValidator,
        SupabaseConfiguration supabaseConfig,
        IHttpClientFactory httpClientFactory,
        CivitiDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Civiti.Auth.Endpoints.SupabaseCallback");

        var code = httpContext.Request.Query["code"].FirstOrDefault();
        var protectedState = httpContext.Request.Query["state"].FirstOrDefault();
        var supabaseError = httpContext.Request.Query["error"].FirstOrDefault();

        if (!string.IsNullOrEmpty(supabaseError))
        {
            var description = httpContext.Request.Query["error_description"].FirstOrDefault();
            logger.LogWarning("Supabase callback returned error {Error}: {Description}", supabaseError, description);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Login failed",
                detail: $"Supabase rejected the login: {supabaseError}");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(protectedState))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid callback",
                detail: "Required 'code' and 'state' query parameters are missing.");
        }

        var state = stateProtector.Unprotect(protectedState);
        if (state is null)
        {
            logger.LogWarning("Rejected /supabase-callback: state missing, expired, or tampered");
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid callback",
                detail: "State parameter is missing, expired, or tampered.");
        }

        var supabaseJwt = await ExchangeCodeAsync(
            code, state.CodeVerifier, supabaseConfig, httpClientFactory, logger, cancellationToken);
        if (supabaseJwt is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Login failed",
                detail: "Supabase rejected the authorization code exchange.");
        }

        var supabasePrincipal = await tokenValidator.ValidateAsync(supabaseJwt, cancellationToken);
        if (supabasePrincipal is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Login failed",
                detail: "Supabase JWT failed validation.");
        }

        var supabaseUserId = supabasePrincipal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            logger.LogWarning("Supabase JWT has no sub claim — refusing login");
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Login failed",
                detail: "Supabase JWT is missing the subject claim.");
        }

        var userProfile = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken);

        if (userProfile is null)
        {
            // §4: Civiti.Auth requires a pre-existing Civica registration; we do not auto-provision
            // a UserProfile from a Supabase login. Sending the user back to /authorize without a
            // session would loop forever, so surface the dead-end with a clear status.
            logger.LogWarning("Login refused — no UserProfile for Supabase sub {Sub}", supabaseUserId);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "No Civiti account",
                detail: "This Supabase user has no matching Civiti profile. Sign up via the Civica app first.");
        }

        var role = ExtractRole(supabasePrincipal);

        var identity = new ClaimsIdentity(AuthEndpointConstants.CookieScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, supabaseUserId));
        identity.AddClaim(new Claim(ClaimTypes.Email, userProfile.Email));
        if (!string.IsNullOrEmpty(role))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        await httpContext.SignInAsync(
            AuthEndpointConstants.CookieScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        logger.LogInformation("Cookie session established for sub {Sub}, role {Role}",
            supabaseUserId, role ?? "(none)");

        // Supabase JWT is no longer needed — by leaving the variable to fall out of scope we
        // satisfy the §4 "immediately discards" intent. There's no separate revocation step;
        // Supabase's session refresh token is never persisted on our side.

        return Results.Redirect(state.ReturnUrl);
    }

    private static async Task<string?> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        SupabaseConfiguration supabaseConfig,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{supabaseConfig.Url.TrimEnd('/')}/auth/v1/token?grant_type=pkce");
            request.Headers.Add("apikey", supabaseConfig.PublishableKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(new
            {
                auth_code = code,
                code_verifier = codeVerifier
            });

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Supabase /token rejected PKCE exchange: {Status} {Body}",
                    response.StatusCode, body);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (!payload.TryGetProperty("access_token", out var accessToken))
            {
                logger.LogWarning("Supabase /token response missing access_token");
                return null;
            }

            return accessToken.GetString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Supabase code exchange threw");
            return null;
        }
    }

    /// <summary>
    /// Supabase places the user role in either the top-level <c>role</c> claim or inside
    /// <c>app_metadata.role</c> depending on project version. <see cref="JsonWebTokenHandler"/>
    /// surfaces the <c>app_metadata</c> object as a serialized JSON string claim, so we parse
    /// it inline. Returns <c>null</c> when neither location yields a value (citizen role).
    /// </summary>
    private static string? ExtractRole(ClaimsPrincipal supabasePrincipal)
    {
        var topLevelRole = supabasePrincipal.FindFirst("role")?.Value;
        if (!string.IsNullOrEmpty(topLevelRole) && topLevelRole != "authenticated")
        {
            return topLevelRole;
        }

        var appMetadata = supabasePrincipal.FindFirst("app_metadata")?.Value;
        if (string.IsNullOrEmpty(appMetadata)) return null;

        try
        {
            using var doc = JsonDocument.Parse(appMetadata);
            if (doc.RootElement.TryGetProperty("role", out var roleProp) && roleProp.ValueKind == JsonValueKind.String)
            {
                return roleProp.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
