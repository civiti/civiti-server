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
                // Verify root is an object before calling TryGetProperty
                if (metadata.RootElement.ValueKind == JsonValueKind.Object
                    && metadata.RootElement.TryGetProperty("role", out var roleElement)
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

    /// <summary>
    /// Gets the user's display name from user_metadata.
    /// Falls back through: full_name → name → email prefix
    /// </summary>
    public static string GetDisplayName(this ClaimsPrincipal user, string? fallbackEmail = null)
    {
        var userMetadata = user.FindFirst("user_metadata")?.Value;
        if (!string.IsNullOrEmpty(userMetadata))
        {
            try
            {
                using var metadata = JsonDocument.Parse(userMetadata);
                if (metadata.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Try full_name first, then name
                    if (metadata.RootElement.TryGetProperty("full_name", out var fullName)
                        && fullName.ValueKind == JsonValueKind.String)
                    {
                        var value = fullName.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }

                    if (metadata.RootElement.TryGetProperty("name", out var name)
                        && name.ValueKind == JsonValueKind.String)
                    {
                        var value = name.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
            }
            catch (JsonException)
            {
                // Invalid JSON, fall through to default
            }
        }

        // Fallback to email prefix
        var email = fallbackEmail ?? user.GetEmail();
        return email?.Split('@')[0] ?? "User";
    }

    /// <summary>
    /// Gets the user's photo URL from user_metadata (avatar_url or picture).
    /// </summary>
    public static string? GetPhotoUrl(this ClaimsPrincipal user)
    {
        var userMetadata = user.FindFirst("user_metadata")?.Value;
        if (!string.IsNullOrEmpty(userMetadata))
        {
            try
            {
                using var metadata = JsonDocument.Parse(userMetadata);
                if (metadata.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (metadata.RootElement.TryGetProperty("avatar_url", out var avatarUrl)
                        && avatarUrl.ValueKind == JsonValueKind.String)
                    {
                        return avatarUrl.GetString();
                    }

                    if (metadata.RootElement.TryGetProperty("picture", out var picture)
                        && picture.ValueKind == JsonValueKind.String)
                    {
                        return picture.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Invalid JSON, fall through to default
            }
        }

        return null;
    }
}
