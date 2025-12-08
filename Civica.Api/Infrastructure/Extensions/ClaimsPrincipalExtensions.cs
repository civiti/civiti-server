using System.Security.Claims;

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
    /// Checks if the user has admin role.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.HasClaim("role", "admin");
    }
}
