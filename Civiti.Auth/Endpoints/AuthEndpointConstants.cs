namespace Civiti.Auth.Endpoints;

/// <summary>
/// Shared identifiers used by the v1b /authorize → Supabase → /supabase-callback round-trip.
/// </summary>
internal static class AuthEndpointConstants
{
    /// <summary>
    /// ASP.NET Core auth scheme name for the short-lived cookie Civiti.Auth issues after a
    /// successful Supabase login. Holds the user's Supabase <c>sub</c> + <c>role</c> so the
    /// next /authorize hit (within 30 minutes) can resume without going back to Supabase.
    /// </summary>
    public const string CookieScheme = "Civiti.Auth.Session";

    /// <summary>
    /// Path Supabase redirects to after the PKCE login round-trip completes. Must be allow-listed
    /// in the Supabase dashboard's <c>Authentication → URL Configuration → Redirect URLs</c> for
    /// each environment's Civiti.Auth origin.
    /// </summary>
    public const string SupabaseCallbackPath = "/supabase-callback";

    /// <summary>
    /// Resource string we attach to issued tokens so Civiti.Mcp's OpenIddict.Validation stack
    /// (lands in v1c) can verify the audience.
    /// </summary>
    public const string ResourceServer = "civiti-mcp";
}
