using System.ComponentModel;
using ModelContextProtocol.Server;
using OpenIddict.Abstractions;

namespace Civiti.Mcp.Tools;

/// <summary>
/// v1c diagnostic tool. Reflects the validated principal back to the caller so an MCP client
/// can confirm the bearer-token handshake end-to-end without needing one of the §2.1 citizen
/// tools (those land in PR 2). Reachable on both <c>/mcp</c> (where it returns the JWT
/// claims) and <c>/mcp/public</c> (where it returns <c>authenticated: false</c>) — same tool
/// either way; the asymmetry is whether OpenIddict.Validation populated a principal upstream.
/// </summary>
[McpServerToolType]
public sealed class WhoAmITools(IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "whoami")]
    [Description("Return the authenticated principal's subject, scopes, and role. Returns authenticated=false on the anonymous /mcp/public endpoint.")]
    public object WhoAmI()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity is not { IsAuthenticated: true })
        {
            return new { authenticated = false };
        }

        // Scope claims: OpenIddict.Validation surfaces scopes both as the standard "scope"
        // claim (RFC 9068) and OpenIddict's private "oi_scp" claim. Read both, dedupe, return
        // a stable shape the diagnostic caller doesn't have to reason about.
        var scopes = user.FindAll(OpenIddictConstants.Claims.Scope)
            .Concat(user.FindAll(OpenIddictConstants.Claims.Private.Scope))
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        return new
        {
            authenticated = true,
            sub = user.FindFirst(OpenIddictConstants.Claims.Subject)?.Value,
            role = user.FindFirst(OpenIddictConstants.Claims.Role)?.Value,
            scopes,
            audience = user.FindAll(OpenIddictConstants.Claims.Audience).Select(c => c.Value).ToArray(),
            issuer = user.FindFirst(OpenIddictConstants.Claims.Issuer)?.Value
        };
    }
}
