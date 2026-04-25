using System.Net;

namespace Civiti.Auth.Startup;

/// <summary>
/// Mounts the proxy-trust middleware via <see cref="IStartupFilter"/> so it runs ahead of
/// OpenIddict's startup filter. The plain <c>app.Use(...)</c> registration we use in
/// Civiti.Mcp / Civiti.Api won't work here: OpenIddict's ASP.NET Core integration injects
/// its server middleware at the top of the pipeline via its own <see cref="IStartupFilter"/>,
/// so any inline middleware sits *behind* it and never sees Railway's
/// <c>X-Forwarded-Proto</c> in time. Without this filter the discovery endpoint
/// short-circuits with ID2083 ("This server only accepts HTTPS requests"). Same trust rules
/// as the inline middleware in the other hosts: rewrite from XFF/XFP only when the direct
/// socket peer is inside Railway's CGNAT range or loopback.
/// </summary>
internal sealed class ProxyTrustStartupFilter : IStartupFilter
{
    private const int RailwayAppendedHopCount = 2;

    private static readonly IPNetwork[] TrustedProxyRanges =
    [
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("::1/128")
    ];

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, requestNext) =>
            {
                var upstream = context.Connection.RemoteIpAddress;
                if (upstream is { IsIPv4MappedToIPv6: true })
                {
                    upstream = upstream.MapToIPv4();
                }

                if (upstream is not null && TrustedProxyRanges.Any(n => n.Contains(upstream)))
                {
                    if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues) && xffValues.Count > 0)
                    {
                        var entries = xffValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (entries.Length >= RailwayAppendedHopCount)
                        {
                            var clientEntry = entries[entries.Length - RailwayAppendedHopCount];
                            if (IPAddress.TryParse(clientEntry, out var clientIp))
                            {
                                context.Connection.RemoteIpAddress = clientIp;
                            }
                        }
                    }

                    if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var xfpValues) && xfpValues.Count > 0)
                    {
                        var protoEntries = xfpValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (protoEntries.Length > 0)
                        {
                            var scheme = protoEntries[^1];
                            if (scheme is "http" or "https")
                            {
                                context.Request.Scheme = scheme;
                            }
                        }
                    }
                }

                await requestNext(context);
            });

            next(app);
        };
    }
}
