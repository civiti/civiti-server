using System.Security.Claims;
using Civiti.Auth.Authentication;
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
/// client can react cleanly.
///
/// v1b.4(a): the OAuth context (clientId, redirect_uri, state, allowed scopes, original
/// /authorize URL) flows through a Data-Protection-encrypted cookie set by /authorize, not
/// through a form-posted <c>ReturnUrl</c> the user could edit before submit. Every render and
/// POST decision reads from <see cref="ConsentContextProtector"/>; the form carries no payload
/// other than the antiforgery token. A user tampering with the cookie value gets a
/// <c>CryptographicException</c>-driven null and a 400.
///
/// "Remember this decision" toggle is intentionally absent: the preference row is always
/// persisted, so a user-visible toggle would be misleading. Per-session (ephemeral) consent
/// requires a <c>RememberedAt</c> column + cleanup sweep tied to cookie expiry — that's
/// v1b.4(b) work, blocked on the not-yet-built Connected AI Assistants UI which would let
/// users see and revoke remembered grants.
/// </summary>
public sealed class ConsentModel(
    IOpenIddictApplicationManager applicationManager,
    AdminScopeFilter adminScopeFilter,
    ConsentContextProtector consentContextProtector,
    CivitiDbContext dbContext,
    ILogger<ConsentModel> logger) : PageModel
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientDisplayName { get; set; } = string.Empty;

    public bool IsTrusted { get; set; }

    public IReadOnlyList<ScopeDescriptor> Scopes { get; set; } = [];

    public IReadOnlyList<string> StrippedScopes { get; set; } = [];

    // OnGet renders this to a hidden field; the POST handlers check it matches the cookie's
    // nonce. Keeps two simultaneous /authorize flows in the same browser (e.g. two tabs for
    // different clients) from clobbering each other — the second tab's cookie overwrites the
    // first's, so the first tab's stale form Nonce won't match and the POST returns 400.
    [BindProperty]
    public string? Nonce { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var ctx = ReadContext();
        if (ctx is null)
        {
            return BadRequest("Consent context missing or expired. Restart the OAuth flow.");
        }

        var application = await applicationManager.FindByClientIdAsync(ctx.ClientId, cancellationToken);
        if (application is null)
        {
            return BadRequest($"Unknown client: {ctx.ClientId}");
        }

        ClientId = ctx.ClientId;
        Nonce = ctx.Nonce;
        ClientDisplayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken)
            ?? ctx.ClientId;
        // Trust badge tracks the `civiti.is_allow_listed` property the seeder stamps on every
        // pre-registered client. Once DCR lands, dynamically-registered apps won't carry this
        // marker and will render the "Unverified" badge automatically — keeping the user-facing
        // trust signal honest without a code change.
        var properties = await applicationManager.GetPropertiesAsync(application, cancellationToken);
        IsTrusted = properties.TryGetValue("civiti.is_allow_listed", out var trustElement)
                    && trustElement.ValueKind == System.Text.Json.JsonValueKind.True;

        // The ctx already carries the post-filter scope set, but re-running the filter is
        // defence-in-depth: a role change or allow-list edit during the 10-min cookie window
        // would otherwise let a user about-to-be-demoted see admin scopes on the consent screen.
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var filterResult = await adminScopeFilter.FilterAsync(
            ctx.ClientId, userRole, ctx.AllowedScopes, cancellationToken);

        Scopes = filterResult.Allowed.Select(ScopeDescriptor.For).ToList();
        StrippedScopes = filterResult.Stripped;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var ctx = ReadContext();
        if (ctx is null)
        {
            return BadRequest("Consent context missing or expired. Restart the OAuth flow.");
        }

        if (!string.Equals(Nonce, ctx.Nonce, StringComparison.Ordinal))
        {
            // Form's hidden Nonce doesn't match the cookie's nonce — most likely a concurrent
            // OAuth flow in another tab overwrote the cookie between this page's render and
            // submit. Refusing protects the user from silently approving consent for a different
            // client than the one shown.
            logger.LogWarning("Consent POST: nonce mismatch (cookie minted a fresh flow during render); refusing");
            return BadRequest("This consent form is stale. Restart the OAuth flow.");
        }

        var supabaseUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            // Cookie session evaporated between consent render and submit — bounce back through
            // /authorize, which will redirect to /Login. The ctx-encoded URL is the only safe
            // source of truth for the redirect target.
            return LocalRedirect(ctx.AuthorizeUrl);
        }

        // Re-run the admin filter on POST so a role change between /authorize (when ctx was
        // minted) and POST is reflected. The filter only strips, never adds, so the result is
        // always a subset of ctx.AllowedScopes.
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var filterResult = await adminScopeFilter.FilterAsync(
            ctx.ClientId, userRole, ctx.AllowedScopes, cancellationToken);

        // Distinct guards against the edge case of duplicate tokens in the upstream `?scope=`
        // parameter (RFC 6749 doesn't forbid duplicates). HasConsentForScopesAsync is safe
        // either way (it ToHashSets), but a clean preference row keeps the union-on-re-consent
        // path in UpsertPreferenceAsync from persisting duplicates forever.
        var grantedScopes = filterResult.Allowed
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await UpsertPreferenceAsync(supabaseUserId, ctx.ClientId, grantedScopes, cancellationToken);

        DeleteContextCookie();

        logger.LogInformation(
            "Consent granted: sub {Sub}, client {ClientId}, scopes={Scopes}",
            supabaseUserId, ctx.ClientId, string.Join(',', grantedScopes));

        return LocalRedirect(ctx.AuthorizeUrl);
    }

    public async Task<IActionResult> OnPostDenyAsync(CancellationToken cancellationToken)
    {
        var ctx = ReadContext();
        if (ctx is null)
        {
            return BadRequest("Consent context missing or expired. Restart the OAuth flow.");
        }

        if (!string.Equals(Nonce, ctx.Nonce, StringComparison.Ordinal))
        {
            // Same concurrent-tab guard as OnPostAsync: refuse Deny for a stale form so we don't
            // emit access_denied to the wrong client's redirect_uri.
            logger.LogWarning("Consent Deny: nonce mismatch; refusing");
            return BadRequest("This consent form is stale. Restart the OAuth flow.");
        }

        // Sign the user out of the Civiti.Auth cookie session — refusing consent should not leave
        // them transparently signed in for a future /authorize hit from the same client.
        await HttpContext.SignOutAsync(AuthEndpointConstants.CookieScheme);

        // OAuth 2.0 §4.1.2.1 — return access_denied to the client's registered redirect_uri.
        // ctx.RedirectUri was OpenIddict-validated at /authorize entry, but re-validate here as
        // defence-in-depth (the registered list could have been edited between /authorize and
        // now). Loopback wildcard mirrors the LoopbackRedirectUriHandlers used by /authorize and
        // /token so native MCP clients (claude-desktop / claude-code at 127.0.0.1:N) receive a
        // clean access_denied bounce instead of a 400.
        var application = await applicationManager.FindByClientIdAsync(ctx.ClientId, cancellationToken);
        if (application is null)
        {
            return BadRequest("Cannot return error to an unrecognised redirect_uri.");
        }

        var redirectUriValid = await applicationManager.ValidateRedirectUriAsync(application, ctx.RedirectUri, cancellationToken)
            || await LoopbackRedirectUriMatcher.MatchesAsync(applicationManager, application, ctx.RedirectUri, cancellationToken);
        if (!redirectUriValid)
        {
            return BadRequest("Cannot return error to an unrecognised redirect_uri.");
        }

        var separator = ctx.RedirectUri.Contains('?') ? '&' : '?';
        var url = $"{ctx.RedirectUri}{separator}error=access_denied&error_description=The+user+denied+the+request";
        if (!string.IsNullOrEmpty(ctx.State))
        {
            url += $"&state={Uri.EscapeDataString(ctx.State)}";
        }

        DeleteContextCookie();

        logger.LogInformation("Consent denied: sub {Sub}, client {ClientId}",
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "(anonymous)", ctx.ClientId);

        return Redirect(url);
    }

    private ConsentContext? ReadContext()
    {
        if (!Request.Cookies.TryGetValue(AuthEndpointConstants.ConsentContextCookie, out var protectedCtx)
            || string.IsNullOrEmpty(protectedCtx))
        {
            return null;
        }
        return consentContextProtector.Unprotect(protectedCtx);
    }

    private void DeleteContextCookie()
    {
        // One-time use: any successful POST consumes the cookie, so a stolen value can't be
        // replayed across multiple consent submits and a back-button revisit doesn't render
        // stale state.
        Response.Cookies.Delete(
            AuthEndpointConstants.ConsentContextCookie,
            new CookieOptions { Path = AuthEndpointConstants.ConsentPath });
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
