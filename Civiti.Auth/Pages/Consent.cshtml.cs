using System.Collections.Specialized;
using System.Security.Claims;
using System.Web;
using Civiti.Auth.Endpoints;
using Civiti.Domain.Entities;
using Civiti.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Civiti.Auth.Pages;

/// <summary>
/// Civiti.Auth's OAuth consent screen (auth-design.md §7). Renders client display name +
/// trust badge + the requested scopes + GDPR notice. Reached when /authorize sees a cookie
/// session but no <see cref="McpUserClientPreference"/> covering the requested scopes.
///
/// All-or-nothing grant: Approve writes a preference row covering every scope the
/// <see cref="AdminScopeFilter"/> allowed for this user+client (matching the all-or-nothing
/// check in <see cref="AuthorizeEndpoint"/>'s <c>HasConsentForScopesAsync</c>); Deny redirects
/// back to the client's <c>redirect_uri</c> with <c>error=access_denied</c> so the OAuth
/// client can react cleanly. Partial-scope grants would require splitting the requested set
/// against the stored set inside <c>HasConsentForScopesAsync</c> + driving a follow-up
/// /Consent visit for any new scope, which we don't ship today.
/// </summary>
public sealed class ConsentModel(
    IOpenIddictApplicationManager applicationManager,
    AdminScopeFilter adminScopeFilter,
    CivitiDbContext dbContext,
    ILogger<ConsentModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public bool RememberClient { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string ClientDisplayName { get; set; } = string.Empty;

    public bool IsTrusted { get; set; }

    public IReadOnlyList<ScopeDescriptor> Scopes { get; set; } = [];

    public IReadOnlyList<string> StrippedScopes { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!IsSafeReturnUrl(ReturnUrl))
        {
            return BadRequest("Invalid returnUrl.");
        }

        var oauthParams = ParseOAuthParams(ReturnUrl);
        if (oauthParams is null)
        {
            return BadRequest("returnUrl is not a recognised /authorize URL.");
        }

        var application = await applicationManager.FindByClientIdAsync(oauthParams.ClientId, cancellationToken);
        if (application is null)
        {
            return BadRequest($"Unknown client: {oauthParams.ClientId}");
        }

        ClientId = oauthParams.ClientId;
        ClientDisplayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken)
            ?? oauthParams.ClientId;
        // Trust badge tracks the `civiti.is_allow_listed` property the seeder stamps on every
        // pre-registered client. Once DCR lands, dynamically-registered apps won't carry this
        // marker and will render the "Unverified" badge automatically — keeping the user-facing
        // trust signal honest without a code change.
        var properties = await applicationManager.GetPropertiesAsync(application, cancellationToken);
        IsTrusted = properties.TryGetValue("civiti.is_allow_listed", out var trustElement)
                    && trustElement.ValueKind == System.Text.Json.JsonValueKind.True;

        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var filterResult = await adminScopeFilter.FilterAsync(
            ClientId, userRole, oauthParams.Scopes, cancellationToken);

        Scopes = filterResult.Allowed.Select(ScopeDescriptor.For).ToList();
        StrippedScopes = filterResult.Stripped;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!IsSafeReturnUrl(ReturnUrl))
        {
            return BadRequest("Invalid returnUrl.");
        }

        var oauthParams = ParseOAuthParams(ReturnUrl);
        if (oauthParams is null)
        {
            return BadRequest("returnUrl is not a recognised /authorize URL.");
        }

        var supabaseUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            // Cookie session evaporated between consent render and submit — bounce back through
            // /authorize, which will redirect to /Login.
            return LocalRedirect(ReturnUrl ?? "/");
        }

        // Approve grants the full set of scopes the AdminScopeFilter allowed — same set the page
        // rendered. Per-scope toggles were considered for v1b.2 but the implementation required
        // partial-grant semantics in HasConsentForScopesAsync that we don't have, so /authorize
        // would loop back to /Consent forever after a partial approve. Industry consent screens
        // (Google, GitHub, Microsoft) ship as all-or-nothing for the same reason — a user who
        // wants to limit access uses Deny + a different client, not a smaller scope subset on
        // the same flow.
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var filterResult = await adminScopeFilter.FilterAsync(
            oauthParams.ClientId, userRole, oauthParams.Scopes, cancellationToken);

        var grantedScopes = filterResult.Allowed.ToList();

        await UpsertPreferenceAsync(supabaseUserId, oauthParams.ClientId, grantedScopes, cancellationToken);

        logger.LogInformation(
            "Consent granted: sub {Sub}, client {ClientId}, scopes={Scopes}, remember={Remember}",
            supabaseUserId, oauthParams.ClientId, string.Join(',', grantedScopes), RememberClient);

        return LocalRedirect(ReturnUrl ?? "/");
    }

    public async Task<IActionResult> OnPostDenyAsync(CancellationToken cancellationToken)
    {
        var oauthParams = ParseOAuthParams(ReturnUrl);
        if (oauthParams is null)
        {
            return BadRequest("returnUrl is not a recognised /authorize URL.");
        }

        // Sign the user out of the Civiti.Auth cookie session — refusing consent should not leave
        // them transparently signed in for a future /authorize hit from the same client.
        await HttpContext.SignOutAsync(AuthEndpointConstants.CookieScheme);

        // OAuth 2.0 §4.1.2.1 — return access_denied to the client's registered redirect_uri.
        // We do NOT redirect to client-supplied URIs without first verifying it matches the
        // app's registered redirect_uri, since this page can be reached by any signed-in user.
        var application = await applicationManager.FindByClientIdAsync(oauthParams.ClientId, cancellationToken);
        if (application is null || !await applicationManager.ValidateRedirectUriAsync(application, oauthParams.RedirectUri, cancellationToken))
        {
            return BadRequest("Cannot return error to an unrecognised redirect_uri.");
        }

        var separator = oauthParams.RedirectUri.Contains('?') ? '&' : '?';
        var url = $"{oauthParams.RedirectUri}{separator}error=access_denied&error_description=The+user+denied+the+request";
        if (!string.IsNullOrEmpty(oauthParams.State))
        {
            url += $"&state={Uri.EscapeDataString(oauthParams.State)}";
        }

        logger.LogInformation("Consent denied: sub {Sub}, client {ClientId}",
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "(anonymous)", oauthParams.ClientId);

        return Redirect(url);
    }

    private async Task UpsertPreferenceAsync(
        string supabaseUserId,
        string clientId,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.McpUserClientPreferences
            .FirstOrDefaultAsync(
                p => p.SupabaseUserId == supabaseUserId && p.ClientId == clientId,
                cancellationToken);

        if (existing is null)
        {
            dbContext.McpUserClientPreferences.Add(new McpUserClientPreference
            {
                Id = Guid.NewGuid(),
                SupabaseUserId = supabaseUserId,
                ClientId = clientId,
                ScopesGranted = scopes.ToList(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            // Union previous + new approved scopes so a re-consent for additional scopes adds
            // rather than overwrites; if the user wants to revoke a scope they do it from the
            // (forthcoming) Connected AI Assistants UI, not by re-running /authorize.
            existing.ScopesGranted = existing.ScopesGranted
                .Union(scopes, StringComparer.Ordinal)
                .ToList();
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static OAuthParams? ParseOAuthParams(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) return null;

        // returnUrl is relative ("/authorize?client_id=...&..."); split off the query manually
        // because Uri-parsing requires an absolute URL.
        var queryStart = returnUrl.IndexOf('?');
        if (queryStart < 0) return null;
        var path = returnUrl[..queryStart];
        if (!string.Equals(path, "/authorize", StringComparison.Ordinal)) return null;

        var query = HttpUtility.ParseQueryString(returnUrl[(queryStart + 1)..]);
        var clientId = query["client_id"];
        var redirectUri = query["redirect_uri"];
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri)) return null;

        var scopeRaw = query["scope"] ?? string.Empty;
        var scopes = scopeRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new OAuthParams(clientId, redirectUri, query["state"], scopes);
    }

    // Consent always needs a /authorize URL to resume — null/empty rejected. Reject
    // protocol-relative URLs (`//evil.com`) and any absolute scheme too: see Login.cshtml.cs's
    // helper for the rationale (Uri.TryCreate accepts `//evil.com` as a valid relative URI per
    // RFC 3986 §4.2 even though browsers resolve it to a remote host).
    private static bool IsSafeReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal)) return false;
        return Uri.TryCreate(url, UriKind.Relative, out _);
    }

    private sealed record OAuthParams(string ClientId, string RedirectUri, string? State, IReadOnlyList<string> Scopes);

    public sealed record ScopeDescriptor(string Name, string DisplayName, string Description)
    {
        public static ScopeDescriptor For(string name) => name switch
        {
            "civiti.read" => new(name, "Read your civic data",
                "List issues, view authorities, see your profile and gamification."),
            "civiti.write" => new(name, "Take actions on your behalf",
                "Submit issues, vote, comment, mark petition emails as sent."),
            "civiti.admin.read" => new(name, "Read moderation queues (admin)",
                "Inspect pending issues and admin action history. Admin-only."),
            "civiti.admin.write" => new(name, "Approve / reject content (admin)",
                "Approve or reject pending issues, take admin actions. Admin-only."),
            "openid" => new(name, "Verify your identity",
                "Confirm who you are with Civiti."),
            "offline_access" => new(name, "Stay signed in",
                "Refresh the connection without re-authenticating every 15 minutes."),
            _ => new(name, name, "Unknown scope.")
        };
    }
}
