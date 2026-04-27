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
    /// Cookie carrying the Data-Protection-encrypted <c>SupabasePkceState</c> blob (CodeVerifier
    /// + ReturnUrl + IssuedAt) across the Supabase round-trip. We can't put this in the OAuth
    /// <c>state</c> query parameter: GoTrue's PKCE-aware <c>/callback</c> validator interprets
    /// <c>state</c> as a Supabase-generated flow-state lookup key (UUID-shaped) and rejects
    /// anything else with <em>"400: OAuth state parameter is invalid"</em>, falling back to Site
    /// URL. Cookie scope keeps the value bound to the same browser that started the login
    /// (CSRF defense) and confines it to <see cref="SupabaseCallbackPath"/>.
    /// </summary>
    public const string SupabasePkceCookie = "civiti_auth_pkce";

    /// <summary>
    /// Resource string we attach to issued tokens so Civiti.Mcp's OpenIddict.Validation stack
    /// (lands in v1c) can verify the audience.
    /// </summary>
    public const string ResourceServer = "civiti-mcp";

    /// <summary>
    /// Env var holding the canonical scheme+host this Civiti.Auth deploy answers on (e.g.
    /// <c>https://civiti-auth-production.up.railway.app</c>). Used to build the
    /// <c>redirect_to</c> URL we hand to Supabase, instead of trusting <c>Request.Host</c>
    /// directly: a spoofed Host header could otherwise steer Supabase at an attacker-controlled
    /// domain (the Supabase redirect-URL allow-list is the second line of defence, but only if
    /// it's tight). Optional — when unset we fall back to Request.Scheme + Request.Host with a
    /// warning so local dev still works without extra ceremony.
    /// </summary>
    public const string PublicOriginEnvVar = "CIVITI_AUTH_PUBLIC_ORIGIN";
}
