using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// Adds <c>registration_endpoint</c> to the <c>/.well-known/openid-configuration</c> +
/// <c>/.well-known/oauth-authorization-server</c> response. OpenIddict 7.5 doesn't have
/// built-in DCR support, so it never advertises the field — and Claude Desktop's Connect
/// flow then concludes the AS has no DCR endpoint and sits there forever waiting for a
/// registration response that's never going to happen.
///
/// The implementation in <see cref="RegisterEndpoint"/> is a hand-rolled ASP.NET Core
/// route; this handler just makes the AS metadata point at it. Building the URL from
/// <c>HttpContext.Request</c> is safe because <c>ProxyTrustStartupFilter</c> has already
/// rewritten <c>Scheme</c> and <c>Host</c> to the public-facing values from XFF — the
/// same trust path the rest of the OpenIddict discovery doc uses for its endpoint URLs.
/// </summary>
internal sealed class AdvertiseRegistrationEndpointHandler(IHttpContextAccessor accessor)
    : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return ValueTask.CompletedTask;
        }

        var registrationUrl = $"{http.Request.Scheme}://{http.Request.Host}{RegisterEndpoint.Path}";
        context.Metadata["registration_endpoint"] = registrationUrl;
        return ValueTask.CompletedTask;
    }
}
