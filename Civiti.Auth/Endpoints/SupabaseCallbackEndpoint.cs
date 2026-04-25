using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Civiti.Auth.Authentication;
using Civiti.Infrastructure.Configuration;

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
        SupabaseLoginCompletion loginCompletion,
        SupabaseConfiguration supabaseConfig,
        IHttpClientFactory httpClientFactory,
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

        var completion = await loginCompletion.CompleteAsync(httpContext, supabaseJwt, cancellationToken);
        return completion switch
        {
            SupabaseLoginCompletion.Result.Ok => Results.Redirect(state.ReturnUrl),
            SupabaseLoginCompletion.Result.NoProfile => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "No Civiti account",
                detail: "This Supabase user has no matching Civiti profile. Sign up via the Civica app first."),
            SupabaseLoginCompletion.Result.Failure failure => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Login failed",
                detail: failure.Detail),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Login failed")
        };
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

}
