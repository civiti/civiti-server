using System.Security.Claims;
using System.Text.Json;

namespace Civica.Api.Infrastructure.Extensions;

/// <summary>
/// Extension methods for extracting claims from JWT tokens.
/// Note: MapInboundClaims is disabled in Program.cs, so claims retain their original JWT names
/// (e.g., "sub" instead of ClaimTypes.NameIdentifier, "email" instead of ClaimTypes.Email)
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the Supabase user ID from the JWT "sub" claim.
    /// </summary>
    public static string? GetSupabaseUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst("sub")?.Value;
    }

    /// <summary>
    /// Gets the user's email from the JWT "email" claim.
    /// </summary>
    public static string? GetEmail(this ClaimsPrincipal user)
    {
        return user.FindFirst("email")?.Value;
    }

    /// <summary>
    /// Checks if the user has admin role from app_metadata.
    /// Supabase stores custom claims in app_metadata, not as top-level JWT claims.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return GetRole(user) == "admin";
    }

    /// <summary>
    /// Gets the user's role from Supabase app_metadata.
    /// Supabase JWT structure: { "app_metadata": { "role": "admin" } }
    /// Returns "user" if no custom role is set.
    /// Note: The top-level "role" claim in Supabase is "authenticated"/"anon" (system use).
    /// </summary>
    public static string GetRole(this ClaimsPrincipal user)
    {
        var appMetadata = user.FindFirst("app_metadata")?.Value;
        if (!string.IsNullOrEmpty(appMetadata))
        {
            try
            {
                using var metadata = JsonDocument.Parse(appMetadata);
                if (metadata.RootElement.TryGetProperty("role", out var roleElement)
                    && roleElement.ValueKind == JsonValueKind.String)
                {
                    return roleElement.GetString() ?? "user";
                }
            }
            catch (JsonException)
            {
                // Invalid JSON, fall through to default
            }
        }

        return "user";
    }
}
