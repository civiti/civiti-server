using System.Security.Claims;
using OpenIddict.Abstractions;

namespace Civiti.Mcp.Authorization;

public static class ScopeClaimExtensions
{
    /// <summary>
    /// Tests whether the principal carries a given Civiti scope, robust to how
    /// OpenIddict.Validation actually populates the claims:
    ///
    /// - The RFC 9068 standard <c>scope</c> claim arrives as a single space-separated string.
    /// - OpenIddict's private <c>oi_scp</c> claim arrives one-per-claim.
    ///
    /// In practice OpenIddict.Validation surfaces both. Matching either keeps us tolerant of
    /// future configuration changes upstream and matches what <c>WhoAmITools</c> already does.
    /// </summary>
    public static bool HasCivitiScope(this ClaimsPrincipal user, string requiredScope)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(requiredScope))
        {
            return false;
        }

        foreach (var claim in user.FindAll(OpenIddictConstants.Claims.Scope)
            .Concat(user.FindAll(OpenIddictConstants.Claims.Private.Scope)))
        {
            foreach (var token in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(token, requiredScope, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
