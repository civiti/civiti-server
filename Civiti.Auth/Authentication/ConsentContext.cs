using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Civiti.Auth.Authentication;

/// <summary>
/// Tamper-resistant snapshot of the OAuth context that <c>/authorize</c> hands to <c>/Consent</c>.
/// Encrypted via ASP.NET Core Data Protection and stashed in a browser cookie scoped to
/// <c>/Consent</c>; the page reads it for every render and POST decision so a user editing the
/// form's hidden fields can't change which client / scopes / redirect_uri the consent applies to.
///
/// Captures the absolute minimum needed to (a) round-trip back to <c>/authorize</c> on Approve
/// and (b) emit an <c>access_denied</c> redirect to the originating client on Deny — without
/// ever trusting a query-string / form value that travelled through the user-controlled browser.
/// </summary>
/// <param name="AuthorizeUrl">
/// The original <c>/authorize?...</c> path+query string. On Approve we <c>LocalRedirect</c> here
/// so OpenIddict re-evaluates the (now satisfied) consent and mints the auth code.
/// </param>
/// <param name="ClientId">Client identity the consent row is keyed against.</param>
/// <param name="RedirectUri">Client redirect URI used by the Deny flow's access_denied bounce.</param>
/// <param name="State">Original OAuth <c>state</c> param, round-tripped on Deny per RFC 6749.</param>
/// <param name="AllowedScopes">
/// Post-AdminScopeFilter scope set the user is being asked to approve; persisted verbatim into
/// <c>McpUserClientPreference.ScopesGranted</c> on Approve. Pre-filtered server-side so a user
/// can't tamper themselves into civiti.admin.* scopes their role doesn't qualify for.
/// </param>
/// <param name="IssuedAtUnix">Mint time; the protector rejects anything older than 10 min.</param>
public sealed record ConsentContext(
    string AuthorizeUrl,
    string ClientId,
    string RedirectUri,
    string? State,
    IReadOnlyList<string> AllowedScopes,
    long IssuedAtUnix);

public sealed class ConsentContextProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "Civiti.Auth.ConsentContext.v1";

    // Same 10-min ceiling as SupabasePkceStateProtector — generous for a slow consent click,
    // tight enough that a stolen cookie is a narrow window. The cookie's MaxAge is set to the
    // same value at the call site for browser-side eviction.
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(ConsentContext context)
    {
        var json = JsonSerializer.Serialize(context);
        return _protector.Protect(json);
    }

    public ConsentContext? Unprotect(string protectedContext)
    {
        try
        {
            var json = _protector.Unprotect(protectedContext);
            var ctx = JsonSerializer.Deserialize<ConsentContext>(json);
            if (ctx is null) return null;

            var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ctx.IssuedAtUnix;
            if (ageSeconds < 0 || ageSeconds > (long)MaxAge.TotalSeconds) return null;

            return ctx;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }
}
