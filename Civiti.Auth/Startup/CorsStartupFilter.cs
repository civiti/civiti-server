namespace Civiti.Auth.Startup;

/// <summary>
/// Mounts <c>app.UseCors(...)</c> via <see cref="IStartupFilter"/> so it runs ahead of
/// OpenIddict's startup filter. Same reason <see cref="ProxyTrustStartupFilter"/> exists:
/// OpenIddict's ASP.NET Core integration injects its server middleware at the top of the
/// pipeline via its own filter, so a plain inline <c>app.UseCors()</c> after
/// <c>builder.Build()</c> sits behind it. The OPTIONS preflight on cross-origin POSTs to
/// <c>/token</c> would then hit OpenIddict first — which interprets it as an
/// <c>invalid_request</c> token request and 400s — before CORS gets a chance to respond
/// with 204 + <c>Access-Control-Allow-*</c> headers.
///
/// Without this filter, Claude Desktop's "Connect Custom Connector" flow (which runs in a
/// claude.ai webview and so issues every fetch as cross-origin) fails with "Couldn't reach
/// the MCP server" before the OAuth dance even starts.
///
/// The CORS policy itself ("<c>Claude</c>") is registered against the DI container in
/// <c>Program.cs</c> alongside the other service registrations.
/// </summary>
internal sealed class CorsStartupFilter : IStartupFilter
{
    public const string PolicyName = "Claude";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseCors(PolicyName);
            next(app);
        };
    }
}
