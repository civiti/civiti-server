using System.Security.Claims;
using System.Text.Json;

namespace Civiti.Api.Infrastructure.Extensions;

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
    private static string GetRole(this ClaimsPrincipal user)
    {
        var appMetadata = user.FindFirst("app_metadata")?.Value;
        
        if (string.IsNullOrEmpty(appMetadata)) return "user";
        
        try
        {
            using JsonDocument metadata = JsonDocument.Parse(appMetadata);
            // Verify root is an object before calling TryGetProperty
            if (metadata.RootElement.ValueKind == JsonValueKind.Object
                && metadata.RootElement.TryGetProperty("role", out JsonElement roleElement)
                && roleElement.ValueKind == JsonValueKind.String)
            {
                return roleElement.GetString() ?? "user";
            }
        }
        catch (JsonException)
        {
            // Invalid JSON, fall through to default
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
                using JsonDocument metadata = JsonDocument.Parse(userMetadata);
                if (metadata.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Try full_name first, then name
                    var result = GetStringProperty(metadata.RootElement, "full_name")
                                 ?? GetStringProperty(metadata.RootElement, "name");
                    if (result is not null) return result;
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
    /// Returns null if the URL is empty or whitespace.
    /// </summary>
    public static string? GetPhotoUrl(this ClaimsPrincipal user)
    {
        var userMetadata = user.FindFirst("user_metadata")?.Value;
        if (!string.IsNullOrEmpty(userMetadata))
        {
            try
            {
                using JsonDocument metadata = JsonDocument.Parse(userMetadata);
                if (metadata.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var result = GetStringProperty(metadata.RootElement, "avatar_url")
                                 ?? GetStringProperty(metadata.RootElement, "picture");
                    if (result is not null) return result;
                }
            }
            catch (JsonException)
            {
                // Invalid JSON, fall through to default
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts signup metadata from user_metadata in the JWT.
    /// These are set during signup via Supabase's data property and become user_metadata claims.
    /// Includes location fields and notification preferences collected during registration.
    /// Returns null if no signup-specific metadata is present (e.g. OAuth logins).
    /// </summary>
    public static SignupMetadata? GetSignupMetadata(this ClaimsPrincipal user)
    {
        var userMetadata = user.FindFirst("user_metadata")?.Value;
        if (string.IsNullOrEmpty(userMetadata))
            return null;

        try
        {
            using JsonDocument metadata = JsonDocument.Parse(userMetadata);
            if (metadata.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            // Location fields
            var county = GetStringProperty(metadata.RootElement, "county");
            var city = GetStringProperty(metadata.RootElement, "city");
            var district = GetStringProperty(metadata.RootElement, "district");
            var residenceType = GetStringProperty(metadata.RootElement, "residence_type");

            // Notification preferences
            var issueUpdates = GetBoolProperty(metadata.RootElement, "issue_updates_enabled");
            var communityNews = GetBoolProperty(metadata.RootElement, "community_news_enabled");
            var monthlyDigest = GetBoolProperty(metadata.RootElement, "monthly_digest_enabled");
            var achievements = GetBoolProperty(metadata.RootElement, "achievements_enabled");
            var pushNotifications = GetBoolProperty(metadata.RootElement, "push_notifications_enabled");

            // Only return metadata if at least one signup field was present
            if (county is null && city is null && district is null && residenceType is null
                && issueUpdates is null && communityNews is null && monthlyDigest is null && achievements is null
                && pushNotifications is null)
                return null;

            return new SignupMetadata
            {
                County = county,
                City = city,
                District = district,
                ResidenceType = residenceType,
                IssueUpdatesEnabled = issueUpdates,
                CommunityNewsEnabled = communityNews,
                MonthlyDigestEnabled = monthlyDigest,
                AchievementsEnabled = achievements,
                PushNotificationsEnabled = pushNotifications
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a boolean property from a JSON element, handling both native JSON booleans
    /// and string-encoded booleans ("true"/"false").
    /// </summary>
    private static bool? GetBoolProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(prop.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Extracts a non-empty string property from a JSON element.
    /// Returns null if the property is missing, not a string, or whitespace-only.
    /// </summary>
    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement prop)
            || prop.ValueKind != JsonValueKind.String)
            return null;

        var value = prop.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}