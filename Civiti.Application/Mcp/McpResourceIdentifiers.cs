namespace Civiti.Application.Mcp;

/// <summary>
/// Identifiers shared by Civiti.Auth (the OAuth Authorization Server, which stamps the
/// <c>aud</c> claim onto issued JWT access tokens) and Civiti.Mcp (the Resource Server,
/// whose OpenIddict.Validation stack rejects any token whose audience doesn't match).
/// Kept here in Civiti.Application so the two services can never drift on the literal —
/// a mismatch silently breaks every authenticated MCP request.
/// </summary>
public static class McpResourceIdentifiers
{
    /// <summary>
    /// Audience value attached to JWT access tokens (<c>aud</c> claim) and the value
    /// Civiti.Mcp's validator requires. Mirrored on the issuing side via
    /// <c>principal.SetResources(...)</c> on the OpenIddict <c>ProcessSignIn</c> path
    /// (see <c>Civiti.Auth/Endpoints/AuthorizeEndpoint.cs</c>'s <c>SetResources</c> call).
    ///
    /// <para>
    /// <b>Coupling:</b> Civiti.Auth's <c>options.IgnoreResourcePermissions()</c> in
    /// <c>Program.cs</c> is safe specifically because every issued token's audience is
    /// pinned to this constant via that <c>SetResources</c> call, regardless of any RFC 8707
    /// <c>resource</c> parameter the client sends. If that pin is ever removed (e.g.
    /// switching to URL-derived audiences for multi-MCP support), the
    /// <c>IgnoreResourcePermissions()</c> bypass MUST be revisited at the same time —
    /// otherwise any DCR-registered client could request a token bound to any scope-known
    /// resource without holding a per-resource permission grant.
    /// </para>
    /// </summary>
    public const string Audience = "civiti-mcp";
}
