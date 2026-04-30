using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// Patches gaps in OpenIddict 7.5's <c>/.well-known/openid-configuration</c> /
/// <c>/.well-known/oauth-authorization-server</c> response so MCP clients
/// (Claude Desktop, Code, …) can complete the Connect flow.
///
/// Two patches today:
///
/// <list type="number">
/// <item><b><c>registration_endpoint</c></b> — OpenIddict 7.5 has no built-in DCR support,
/// so the framework never advertises this field. The hand-rolled implementation lives in
/// <see cref="RegisterEndpoint"/>; this handler points the discovery doc at it.</item>
///
/// <item><b><c>"none"</c> in <c>token_endpoint_auth_methods_supported</c></b> — OpenIddict's
/// default <c>AttachClientAuthenticationMethods</c> emits the three confidential-client
/// methods (<c>client_secret_post</c>, <c>private_key_jwt</c>, <c>client_secret_basic</c>)
/// even when public clients are accepted. Per RFC 7591 §5, conformant DCR clients reject an
/// AS whose advertised methods don't cover what they need to use — Claude Desktop, which
/// registers as a public client (<c>token_endpoint_auth_method=none</c>), bails out before
/// ever calling <c>/register</c> if <c>"none"</c> isn't in the advertised list. This handler
/// appends it.</item>
/// </list>
///
/// We add to the typed <c>context.TokenEndpointAuthenticationMethods</c> collection (the
/// one OpenIddict's <c>AttachClientAuthenticationMethods</c> populates), not the
/// <c>Metadata</c> dictionary. Earlier attempts wrote to <c>Metadata</c> directly and
/// silently wiped the three confidential methods, because the dictionary is populated from
/// the typed property in a later phase — at our handler's time it's still empty.
///
/// Building the registration URL from <c>HttpContext.Request</c> is safe because
/// <c>ProxyTrustStartupFilter</c> has already rewritten <c>Scheme</c> and <c>Host</c> to the
/// public-facing values from XFF — the same trust path the rest of the OpenIddict discovery
/// doc uses for its endpoint URLs.
/// </summary>
internal sealed class AdvertiseRegistrationEndpointHandler(IHttpContextAccessor accessor)
    : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    private const string NoneAuthMethod = "none";

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        var http = accessor.HttpContext;
        if (http is not null)
        {
            var registrationUrl = $"{http.Request.Scheme}://{http.Request.Host}{RegisterEndpoint.Path}";
            context.Metadata["registration_endpoint"] = registrationUrl;
        }

        // Append "none" to the typed collection — idempotent if AttachClient already added
        // it (it doesn't today), or if some future OpenIddict change starts emitting it.
        // Order pinning in Program.cs (descriptor.Order + 1_000) keeps us after the
        // default handler so the three confidential methods are already present when we
        // run, but Add is the safe op either way.
        if (!context.TokenEndpointAuthenticationMethods.Contains(NoneAuthMethod))
        {
            context.TokenEndpointAuthenticationMethods.Add(NoneAuthMethod);
        }

        return ValueTask.CompletedTask;
    }
}
