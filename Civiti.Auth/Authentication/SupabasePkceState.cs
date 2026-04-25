using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Civiti.Auth.Authentication;

/// <summary>
/// Minted on /authorize when no Civiti.Auth cookie session exists. Encrypted via ASP.NET Core
/// Data Protection and round-tripped through Supabase as the OAuth <c>state</c> parameter so
/// /supabase-callback can recover the PKCE verifier and the original /authorize query string
/// (and resume the OpenIddict flow from where it left off).
/// </summary>
internal sealed record SupabasePkceState(
    string CodeVerifier,
    string ReturnUrl,
    long IssuedAtUnix);

internal sealed class SupabasePkceStateProtector(IDataProtectionProvider provider)
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
