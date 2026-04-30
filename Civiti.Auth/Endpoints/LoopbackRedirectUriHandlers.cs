using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// RFC 8252 §8.3 loopback port wildcard for native MCP clients (claude-desktop,
/// claude-code). Native apps bind to an ephemeral loopback port at runtime and register a
/// placeholder URI like <c>http://127.0.0.1:0/callback</c>. OpenIddict's built-in
/// redirect_uri validators do exact-string matching against the registered URI, so an
/// incoming <c>http://127.0.0.1:54321/callback</c> is rejected.
///
/// Strategy: <em>replace</em> OpenIddict's built-in <c>ValidateClientRedirectUri</c>
/// (authorization phase) and <c>Exchange.ValidateRedirectUri</c> (token-exchange phase) with
/// the two handlers in this file. Each one keeps the original exact-match check via
/// <see cref="IOpenIddictApplicationManager.ValidateRedirectUriAsync"/> and adds a
/// loopback-wildcard fallback that accepts any port when both the registered and requested
/// URIs are loopback with matching scheme/path/query/fragment.
///
/// Why replace instead of swap-the-URI-then-restore-it: <see cref="ValidateAuthorizationRequestContext.SetRedirectUri"/>
/// is for the fallback case where the client didn't send a redirect_uri at all. Calling it
/// when the client did supply one trips OpenIddict's internal consistency check against
/// <c>Request.RedirectUri</c> and 500s. Replacing the validator entirely avoids touching the
/// request payload.
///
/// Non-loopback redirect URIs (claude-ai HTTPS, chatgpt-connector HTTPS) get the same
/// exact-match treatment as before — port mismatches are meaningful for fixed-host callbacks
/// and we don't want to relax that.
/// </summary>
public sealed class LoopbackAwareAuthorizationRedirectUriValidator(
    IOpenIddictApplicationManager applicationManager,
    ILogger<LoopbackAwareAuthorizationRedirectUriValidator> logger)
    : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        if (string.IsNullOrEmpty(context.RedirectUri) || string.IsNullOrEmpty(context.ClientId))
        {
            return;
        }

        var application = await applicationManager.FindByClientIdAsync(context.ClientId, context.CancellationToken);
        if (application is null)
        {
            // An earlier handler (ValidateClientId) should have already rejected this; bail.
            return;
        }

        if (await applicationManager.ValidateRedirectUriAsync(application, context.RedirectUri, context.CancellationToken))
        {
            return;
        }

        if (await LoopbackRedirectUriMatcher.MatchesAsync(applicationManager, application, context.RedirectUri, context.CancellationToken))
        {
            logger.LogInformation(
                "Loopback wildcard accepted for client {ClientId}: redirect_uri {RedirectUri}",
                context.ClientId, context.RedirectUri);
            return;
        }

        context.Reject(
            error: OpenIddictConstants.Errors.InvalidRequest,
            description: "The specified 'redirect_uri' is not valid for this client application.");
    }
}

/// <summary>
/// Mirror of <see cref="LoopbackAwareAuthorizationRedirectUriValidator"/> for the /token
/// exchange phase. OpenIddict's built-in exchange-side validator also calls
/// <see cref="IOpenIddictApplicationManager.ValidateRedirectUriAsync"/>, so the auth-code
/// exchange would re-fail on the same port mismatch without this replacement.
/// </summary>
public sealed class LoopbackAwareTokenRedirectUriValidator(
    IOpenIddictApplicationManager applicationManager,
    ILogger<LoopbackAwareTokenRedirectUriValidator> logger)
    : IOpenIddictServerHandler<ValidateTokenRequestContext>
{
    public async ValueTask HandleAsync(ValidateTokenRequestContext context)
    {
        // Only authorization_code grants exchange a redirect_uri — refresh_token /
        // client_credentials skip this validator entirely in OpenIddict's pipeline.
        if (!context.Request.IsAuthorizationCodeGrantType()) return;

        // RFC 6749 §4.1.3: the redirect_uri on the token request must mirror what was on the
        // /authorize request that minted the auth code. OpenIddict stores the original
        // redirect_uri on the code's principal as a private claim; we compare presence
        // symmetrically — absent on both is fine, mismatch (or asymmetric absence) rejects.
        // Skipping this check would let an attacker who steals an auth code redeem it without
        // the redirect_uri binding, defeating the whole point of the binding.
        var requestUri = context.Request.RedirectUri;
        var principalUri = context.AuthorizationCodePrincipal?.GetClaim(OpenIddictConstants.Claims.Private.RedirectUri);

        if (string.IsNullOrEmpty(principalUri) && string.IsNullOrEmpty(requestUri))
        {
            return; // Code was minted without a redirect_uri; token request matches.
        }
        if (string.IsNullOrEmpty(requestUri))
        {
            context.Reject(
                error: OpenIddictConstants.Errors.InvalidRequest,
                description: "The mandatory 'redirect_uri' parameter is missing.");
            return;
        }
        if (string.IsNullOrEmpty(principalUri))
        {
            context.Reject(
                error: OpenIddictConstants.Errors.InvalidGrant,
                description: "The 'redirect_uri' parameter is not expected for this authorization code.");
            return;
        }
        if (!string.Equals(principalUri, requestUri, StringComparison.Ordinal))
        {
            context.Reject(
                error: OpenIddictConstants.Errors.InvalidGrant,
                description: "The specified 'redirect_uri' does not match the original authorization request.");
            return;
        }

        // Defense in depth: re-validate the URI against the application's currently-registered
        // URIs, in case the allow-list was edited between /authorize and /token. Loopback
        // wildcard fallback applies here too — the original /authorize call will have been
        // accepted via the same loopback rule, and we want the /token exchange to mirror it.
        if (string.IsNullOrEmpty(context.ClientId)) return;
        var application = await applicationManager.FindByClientIdAsync(context.ClientId, context.CancellationToken);
        if (application is null) return;

        if (await applicationManager.ValidateRedirectUriAsync(application, requestUri, context.CancellationToken))
        {
            return;
        }
        if (await LoopbackRedirectUriMatcher.MatchesAsync(applicationManager, application, requestUri, context.CancellationToken))
        {
            logger.LogInformation(
                "Loopback wildcard accepted on /token for client {ClientId}: redirect_uri {RedirectUri}",
                context.ClientId, requestUri);
            return;
        }

        context.Reject(
            error: OpenIddictConstants.Errors.InvalidGrant,
            description: "The specified 'redirect_uri' is not valid for this client application.");
    }
}

/// <summary>
/// Shared loopback wildcard matcher. RFC 8252 §8.3 only relaxes <em>port</em> — scheme, path,
/// query, and fragment all still have to match exactly.
/// </summary>
public static class LoopbackRedirectUriMatcher
{
    public static async ValueTask<bool> MatchesAsync(
        IOpenIddictApplicationManager applicationManager,
        object application,
        string requestedUriString,
        CancellationToken cancellationToken)
    {
        if (!TryParseLoopback(requestedUriString, out var requestedUri))
        {
            return false;
        }

        var registeredUris = await applicationManager.GetRedirectUrisAsync(application, cancellationToken);
        foreach (var registered in registeredUris)
        {
            if (!TryParseLoopback(registered, out var registeredUri)) continue;
            if (!string.Equals(registeredUri.Scheme, requestedUri.Scheme, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(registeredUri.AbsolutePath, requestedUri.AbsolutePath, StringComparison.Ordinal)) continue;
            if (!string.Equals(registeredUri.Query, requestedUri.Query, StringComparison.Ordinal)) continue;
            if (!string.Equals(registeredUri.Fragment, requestedUri.Fragment, StringComparison.Ordinal)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="raw"/> parses as an absolute URI whose host is the IPv4
    /// loopback (<c>127.0.0.1</c>) or IPv6 loopback (<c>::1</c>). Used by both the
    /// authorize/token validators and <c>RegisterEndpoint</c> (DCR) so all three apply the
    /// same RFC 8252 §8.3 host check.
    /// </summary>
    public static bool IsLoopback(string raw) => TryParseLoopback(raw, out _);

    /// <summary>
    /// Exact-match allowlist for DCR registrations from cloud-relay MCP clients that don't
    /// bind a loopback port locally. Claude Desktop's "Add custom connector" flow round-trips
    /// the OAuth code through Anthropic's infrastructure at
    /// <c>https://claude.ai/api/mcp/auth_callback</c>; that's the public, documented relay
    /// URI used by every other remote MCP server (Asana, Linear, Notion, GitHub). RFC 8252
    /// §8.3's loopback recommendation only applies to native apps that <em>can</em> open a
    /// loopback listener — the cloud-relay model falls outside its scope, so we admit those
    /// URIs via an explicit, narrowly-scoped allowlist instead.
    ///
    /// Append-only with extreme caution: an attacker who registers a redirect URI under a
    /// host they control can complete the auth-code flow and steal access tokens. Every
    /// entry must be a first-party host that the client vendor itself controls and has
    /// publicly documented as their MCP relay endpoint.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowlistedDcrRedirectUris =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "https://claude.ai/api/mcp/auth_callback"
        };

    /// <summary>
    /// True when <paramref name="raw"/> is acceptable as a DCR registration redirect URI:
    /// either a loopback URI (RFC 8252 §8.3) or an exact match in
    /// <see cref="AllowlistedDcrRedirectUris"/>. The two paths are mutually exclusive in
    /// practice (loopback URIs are <c>http://127.0.0.1:.../</c> with arbitrary ports;
    /// allowlisted URIs are HTTPS first-party callbacks) so order doesn't matter.
    /// </summary>
    public static bool IsAcceptableDcrRedirectUri(string raw) =>
        IsLoopback(raw) || AllowlistedDcrRedirectUris.Contains(raw);

    /// <summary>
    /// Loopback per RFC 8252 §8.3: <c>127.0.0.1</c> or <c>::1</c>. <c>localhost</c> is
    /// deliberately excluded — the spec recommends against it because it depends on local
    /// DNS resolution that varies across platforms.
    /// </summary>
    private static bool TryParseLoopback(string raw, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)) return false;
        if (parsed.HostNameType != UriHostNameType.IPv4 && parsed.HostNameType != UriHostNameType.IPv6) return false;
        var host = parsed.Host.Trim('[', ']');
        if (host != "127.0.0.1" && host != "::1") return false;
        uri = parsed;
        return true;
    }
}
