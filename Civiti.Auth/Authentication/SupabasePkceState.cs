using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Civiti.Auth.Authentication;

/// <summary>
/// Minted on /Login when the user starts the Supabase OAuth round-trip. Encrypted via ASP.NET
/// Core Data Protection and stashed in a <see cref="Civiti.Auth.Endpoints.AuthEndpointConstants.SupabasePkceCookie"/>
/// browser cookie so /supabase-callback can recover the PKCE verifier and the original
/// /authorize query string (and resume the OpenIddict flow from where it left off). We can't
/// piggy-back on the OAuth <c>state</c> param because GoTrue's PKCE-aware <c>/callback</c>
/// treats <c>state</c> as its own flow-state lookup key.
/// </summary>
public sealed record SupabasePkceState(
    string CodeVerifier,
    string ReturnUrl,
    long IssuedAtUnix);

public sealed class SupabasePkceStateProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "Civiti.Auth.SupabasePkceState.v1";

    // Bound the redirect window — anything older than this is treated as tampered/replayed and
    // rejected. The Supabase login flow itself is typically <60 s; 10 min is a generous ceiling
    // that accommodates a slow magic-link click.
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(SupabasePkceState state)
    {
        var json = JsonSerializer.Serialize(state);
        return _protector.Protect(json);
    }

    public SupabasePkceState? Unprotect(string protectedState)
    {
        try
        {
            var json = _protector.Unprotect(protectedState);
            var state = JsonSerializer.Deserialize<SupabasePkceState>(json);
            if (state is null) return null;

            var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - state.IssuedAtUnix;
            if (ageSeconds < 0 || ageSeconds > (long)MaxAge.TotalSeconds) return null;

            return state;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }
}
