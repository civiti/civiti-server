using System.Collections.Immutable;
using OpenIddict.Abstractions;
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
/// even when public clients are accepted. Per RFC 7591 §5, conformant DCR clients reject
/// an AS whose advertised methods don't cover what they need to use — Claude Desktop, which
/// registers as a public client (<c>token_endpoint_auth_method=none</c>), bails out before
/// ever calling <c>/register</c> if <c>"none"</c> isn't in the advertised list. This handler
/// appends it.</item>
/// </list>
///
/// Building the registration URL from <c>HttpContext.Request</c> is safe because
/// <c>ProxyTrustStartupFilter</c> has already rewritten <c>Scheme</c> and <c>Host</c> to the
/// public-facing values from XFF — the same trust path the rest of the OpenIddict discovery
/// doc uses for its endpoint URLs.
/// </summary>
internal sealed class AdvertiseRegistrationEndpointHandler(IHttpContextAccessor accessor)
    : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    private const string TokenAuthMethodsKey = "token_endpoint_auth_methods_supported";
    private const string NoneAuthMethod = "none";

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        var http = accessor.HttpContext;
        if (http is not null)
        {
            var registrationUrl = $"{http.Request.Scheme}://{http.Request.Host}{RegisterEndpoint.Path}";
            context.Metadata["registration_endpoint"] = registrationUrl;
        }

        // Append "none" to the advertised auth methods if it isn't already there. We
        // preserve whatever OpenIddict put in the array first so a future framework change
        // that adds a new method just flows through. The current contents are accessible
        // via GetUnnamedParameters() — each child has an explicit string conversion.
        var existingMethods = new List<string>(4);
        if (context.Metadata.TryGetValue(TokenAuthMethodsKey, out var raw))
        {
            foreach (var child in raw.GetUnnamedParameters())
            {
                var value = (string?)child;
                if (value is not null)
                {
                    existingMethods.Add(value);
                }
            }
        }
        if (!existingMethods.Contains(NoneAuthMethod, StringComparer.Ordinal))
        {
            existingMethods.Add(NoneAuthMethod);
            // OpenIddictParameter's implicit conversion targets ImmutableArray<string>, not
            // string[] — the latter is not in the operator overload set despite being the
            // obvious shape. Materialise into the right type so the implicit cast fires.
            context.Metadata[TokenAuthMethodsKey] = ImmutableArray.CreateRange<string?>(existingMethods);
        }

        return ValueTask.CompletedTask;
    }
}
