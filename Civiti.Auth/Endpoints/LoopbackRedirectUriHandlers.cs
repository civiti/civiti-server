using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// RFC 8252 §8.3 loopback port wildcard for native MCP clients (claude-desktop, claude-code).
/// Native apps bind to an ephemeral loopback port at runtime and register a placeholder URI
/// like <c>http://127.0.0.1:0/callback</c>. OpenIddict's default <c>ValidateClientRedirectUri</c>
/// handler does an exact-string match and rejects anything but the registered port — so the
/// real callback URI <c>http://127.0.0.1:54321/callback</c> is rejected.
///
/// Two-handler dance: <see cref="LoopbackAuthorizationRequestValidator"/> runs early in the
/// authorization pipeline. If the incoming redirect_uri is loopback and a registered URI for
/// the same client is also loopback with the same path, we stash the original URI in the
/// OpenIddict transaction's property bag and call <c>SetRedirectUri</c> to swap in the
/// registered form so the built-in exact-match validator passes. Then
/// <see cref="LoopbackAuthorizationResponseRestorer"/> runs in the response phase, reads the
/// stashed original, and sets <c>ApplyAuthorizationResponseContext.RedirectUri</c> back to it
/// so the user-agent is redirected to the actual port the native client is listening on.
///
/// Non-loopback redirect_uris (claude-ai, chatgpt-connector) are untouched — exact match still
/// applies, which is correct for HTTPS callbacks where port mismatches *are* meaningful.
/// </summary>
public sealed class LoopbackAuthorizationRequestValidator(
    IOpenIddictApplicationManager applicationManager,
    ILogger<LoopbackAuthorizationRequestValidator> logger)
    : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public const string OriginalRedirectUriPropertyKey = "Civiti.Auth.LoopbackOriginalRedirectUri";

    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        var requested = context.RedirectUri;
        if (string.IsNullOrEmpty(requested) || string.IsNullOrEmpty(context.ClientId)) return;
        if (!TryParseLoopback(requested, out var requestedUri)) return;

        var application = await applicationManager.FindByClientIdAsync(context.ClientId, context.CancellationToken);
        if (application is null) return;

        var registeredUris = await applicationManager.GetRedirectUrisAsync(application, context.CancellationToken);
        string? registeredMatch = null;
        foreach (var registered in registeredUris)
        {
            if (!TryParseLoopback(registered, out var registeredUri)) continue;
            if (!string.Equals(registeredUri.Scheme, requestedUri.Scheme, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(registeredUri.AbsolutePath, requestedUri.AbsolutePath, StringComparison.Ordinal)) continue;
            // Query + fragment are NOT relaxed by RFC 8252 §8.3 — only the port is.
            // Comparing them keeps a registered ?version=2 callback from accidentally
            // matching an incoming URI without the parameter (or vice versa).
            if (!string.Equals(registeredUri.Query, requestedUri.Query, StringComparison.Ordinal)) continue;
            if (!string.Equals(registeredUri.Fragment, requestedUri.Fragment, StringComparison.Ordinal)) continue;

            registeredMatch = registered;
            break;
        }
        if (registeredMatch is null) return;

        context.Transaction.Properties[OriginalRedirectUriPropertyKey] = requested;
        context.SetRedirectUri(registeredMatch);

        logger.LogInformation(
            "Loopback wildcard accepted for client {ClientId}: original {Original} → registered {Registered}",
            context.ClientId, requested, registeredMatch);
    }

    /// <summary>
    /// Loopback per RFC 8252 §8.3: <c>127.0.0.1</c> or <c>::1</c>. <c>localhost</c> is
    /// deliberately excluded — the spec recommends against it because it depends on local DNS
    /// resolution and resolver configuration that varies across platforms.
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

/// <summary>
/// Restores the original (ephemeral-port) loopback redirect URI on the response so the user
/// agent gets redirected to the actual port the native MCP client is listening on, instead of
/// the port-0 placeholder we swapped in for validation.
/// </summary>
public sealed class LoopbackAuthorizationResponseRestorer
    : IOpenIddictServerHandler<ApplyAuthorizationResponseContext>
{
    public ValueTask HandleAsync(ApplyAuthorizationResponseContext context)
    {
        if (context.Transaction.Properties.TryGetValue(
                LoopbackAuthorizationRequestValidator.OriginalRedirectUriPropertyKey,
                out var stashed)
            && stashed is string original)
        {
            context.RedirectUri = original;
        }
        return ValueTask.CompletedTask;
    }
}
