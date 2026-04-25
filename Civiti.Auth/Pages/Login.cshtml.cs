using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Civiti.Auth.Authentication;
using Civiti.Auth.Endpoints;
using Civiti.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Civiti.Auth.Pages;

/// <summary>
/// Civiti.Auth's local sign-in page. Sits between /authorize and the OpenIddict signin pipeline:
/// when /authorize finds no cookie session, it redirects here with the original /authorize
/// URL in <see cref="ReturnUrl"/>. The page POSTs back via two paths — email+password, and a
/// "Sign in with Google" button that kicks off the existing Supabase PKCE flow.
/// </summary>
public sealed class LoginModel(
    SupabaseConfiguration supabaseConfig,
    SupabaseLoginCompletion loginCompletion,
    SupabasePkceStateProtector stateProtector,
    IHttpClientFactory httpClientFactory,
    ILogger<LoginModel> logger) : PageModel
{
    private const string DefaultProvider = "google";
    private const string ProviderEnvVar = "SUPABASE_LOGIN_PROVIDER";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    public string? ClientDisplayName { get; set; }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (!IsSafeReturnUrl(ReturnUrl))
        {
            // Refuse open redirects — only allow returns to a relative URL on this host.
            return BadRequest("Invalid returnUrl.");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!IsSafeReturnUrl(ReturnUrl))
        {
            return BadRequest("Invalid returnUrl.");
        }
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email and password are both required.";
            return Page();
        }

        var supabaseJwt = await ExchangePasswordAsync(Email, Password, cancellationToken);
        if (supabaseJwt is null)
        {
            ErrorMessage = "Sign-in failed. Check your email and password, or use Google instead.";
            return Page();
        }

        var result = await loginCompletion.CompleteAsync(HttpContext, supabaseJwt, cancellationToken);
        return result switch
        {
            SupabaseLoginCompletion.Result.Ok => LocalRedirect(ReturnUrl ?? "/"),
            SupabaseLoginCompletion.Result.NoProfile => Forbid403("This account isn't registered with Civica yet. Sign up via the Civica app first."),
            _ => RenderRetry("Sign-in failed. Please try again.")
        };
    }

    public IActionResult OnPostGoogle()
    {
        if (!IsSafeReturnUrl(ReturnUrl))
        {
            return BadRequest("Invalid returnUrl.");
        }

        var (verifier, challenge) = PkceCodes.Generate();
        var state = new SupabasePkceState(verifier, ReturnUrl ?? "/", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var protectedState = stateProtector.Protect(state);

        var publicOrigin = Environment.GetEnvironmentVariable(AuthEndpointConstants.PublicOriginEnvVar);
        var callbackUrl = !string.IsNullOrWhiteSpace(publicOrigin)
            ? $"{publicOrigin.TrimEnd('/')}{AuthEndpointConstants.SupabaseCallbackPath}"
            : $"{Request.Scheme}://{Request.Host}{AuthEndpointConstants.SupabaseCallbackPath}";

        if (string.IsNullOrWhiteSpace(publicOrigin))
        {
            logger.LogWarning(
                "CIVITI_AUTH_PUBLIC_ORIGIN unset — falling back to Request.Host for Supabase callback. Set the env var in production to defend against Host-header spoofing.");
        }

        var provider = Environment.GetEnvironmentVariable(ProviderEnvVar) ?? DefaultProvider;

        var supabaseUrl =
            $"{supabaseConfig.Url.TrimEnd('/')}/auth/v1/authorize" +
            $"?provider={Uri.EscapeDataString(provider)}" +
            $"&redirect_to={Uri.EscapeDataString(callbackUrl)}" +
            $"&code_challenge={challenge}" +
            $"&code_challenge_method=S256" +
            $"&state={Uri.EscapeDataString(protectedState)}";

        return Redirect(supabaseUrl);
    }

    private async Task<string?> ExchangePasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{supabaseConfig.Url.TrimEnd('/')}/auth/v1/token?grant_type=password");
            request.Headers.Add("apikey", supabaseConfig.PublishableKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(new { email, password });

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 400 from Supabase = bad credentials. Don't log the response body — it can echo
                // the email back which we don't need on disk.
                logger.LogWarning("Supabase password grant rejected (status {Status})", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (!payload.TryGetProperty("access_token", out var accessToken))
            {
                logger.LogWarning("Supabase password grant response missing access_token");
                return null;
            }
            return accessToken.GetString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Supabase password exchange threw");
            return null;
        }
    }

    private IActionResult RenderRetry(string message)
    {
        ErrorMessage = message;
        return Page();
    }

    private IActionResult Forbid403(string detail)
    {
        return new ObjectResult(new { error = "no_civiti_account", detail })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    // Login accepts a null/empty returnUrl by design — a user landing on /Login directly (not
    // via /authorize) gets bounced to the homepage on success. Consent.cshtml.cs has its own
    // stricter helper that rejects null because it always needs a /authorize URL to resume.
    private static bool IsSafeReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Relative, out _);
    }
}
