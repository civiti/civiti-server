using System.Security.Claims;
using System.Text.Json;
using Civiti.Auth.Endpoints;
using Civiti.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Auth.Authentication;

/// <summary>
/// Shared finishing move for both Civiti.Auth login paths (the Supabase OAuth PKCE callback
/// and the email+password POST on /Login). Validates the Supabase JWT, confirms a matching
/// <c>UserProfile</c> exists, sets the Civiti.Auth session cookie, and discards the Supabase
/// session per auth-design.md §4.
/// </summary>
public sealed class SupabaseLoginCompletion(
    SupabaseTokenValidator tokenValidator,
    CivitiDbContext dbContext,
    ILogger<SupabaseLoginCompletion> logger)
{
    public async Task<Result> CompleteAsync(
        HttpContext httpContext,
        string supabaseJwt,
        CancellationToken cancellationToken)
    {
        var supabasePrincipal = await tokenValidator.ValidateAsync(supabaseJwt, cancellationToken);
        if (supabasePrincipal is null)
        {
            return Result.Failed("Supabase JWT failed validation.");
        }

        var supabaseUserId = supabasePrincipal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            logger.LogWarning("Supabase JWT has no sub claim — refusing login");
            return Result.Failed("Supabase JWT is missing the subject claim.");
        }

        var userProfile = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken);

        if (userProfile is null)
        {
            // §4: no auto-provisioning — the Civica frontend creates UserProfile on signup.
            logger.LogWarning("Login refused — no UserProfile for Supabase sub {Sub}", supabaseUserId);
            return Result.NoCiviciAccount(supabaseUserId);
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

        return Result.Success(supabaseUserId, role);
    }

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

    public abstract record Result
    {
        public sealed record Ok(string SupabaseUserId, string? Role) : Result;
        public sealed record NoProfile(string SupabaseUserId) : Result;
        public sealed record Failure(string Detail) : Result;

        public static Result Success(string supabaseUserId, string? role) => new Ok(supabaseUserId, role);
        public static Result NoCiviciAccount(string supabaseUserId) => new NoProfile(supabaseUserId);
        public static Result Failed(string detail) => new Failure(detail);
    }
}
