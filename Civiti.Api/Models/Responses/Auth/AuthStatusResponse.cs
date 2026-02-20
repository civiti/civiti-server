namespace Civiti.Api.Models.Responses.Auth;

/// <summary>
/// Response returned by the authentication status endpoint
/// </summary>
public class AuthStatusResponse
{
    /// <summary>
    /// Whether the user is currently authenticated
    /// </summary>
    /// <example>true</example>
    public bool Authenticated { get; set; }

    /// <summary>
    /// The user's Supabase user ID from the JWT token
    /// </summary>
    /// <example>a1b2c3d4-e5f6-7890-abcd-ef1234567890</example>
    public string? SupabaseUserId { get; set; }

    /// <summary>
    /// The user's email address from the JWT token
    /// </summary>
    /// <example>ion.popescu@example.com</example>
    public string? Email { get; set; }
}
