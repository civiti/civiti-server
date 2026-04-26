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
        // Only authorization_code grants carry a redirect_uri — refresh_token / client_credentials
        // skip this validator entirely in OpenIddict's pipeline, but be defensive.
        if (!context.Request.IsAuthorizationCodeGrantType()) return;
        if (string.IsNullOrEmpty(context.Request.RedirectUri) || string.IsNullOrEmpty(context.ClientId))
        {
            return;
        }

        var application = await applicationManager.FindByClientIdAsync(context.ClientId, context.CancellationToken);
        if (application is null) return;

        if (await applicationManager.ValidateRedirectUriAsync(application, context.Request.RedirectUri, context.CancellationToken))
        {
            return;
        }

        if (await LoopbackRedirectUriMatcher.MatchesAsync(applicationManager, application, context.Request.RedirectUri, context.CancellationToken))
        {
            logger.LogInformation(
                "Loopback wildcard accepted on /token for client {ClientId}: redirect_uri {RedirectUri}",
                context.ClientId, context.Request.RedirectUri);
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
internal static class LoopbackRedirectUriMatcher
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
