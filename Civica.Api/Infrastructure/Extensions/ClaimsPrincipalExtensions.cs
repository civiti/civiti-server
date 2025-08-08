using System.Security.Claims;

namespace Civica.Api.Infrastructure.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal user)
    {
        var userId = user.FindFirst("sub")?.Value 
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User ID not found in claims");
            
        return userId;
    }
    
    public static string? GetEmail(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Email)?.Value 
            ?? user.FindFirst("email")?.Value;
    }
    
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole("admin") 
            || user.HasClaim("role", "admin")
            || user.HasClaim(ClaimTypes.Role, "admin");
    }
    
    public static string? GetSupabaseUserId(this ClaimsPrincipal user)
    {
        // Supabase JWT stores user ID in the 'sub' claim
        return user.FindFirst("sub")?.Value;
    }
}