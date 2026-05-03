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
    /// <b>Load-bearing:</b> Civiti.Auth's <c>Program.cs</c> removes OpenIddict's built-in
    /// <c>Authentication.ValidateResources</c> + <c>Exchange.ValidateResources</c> handlers,
    /// so the AS does NOT validate that the RFC 8707 <c>resource</c> parameter matches a
    /// known resource. The only thing keeping issued tokens bound to Civiti.Mcp is the
    /// explicit <c>SetResources(Audience)</c> call in <c>AuthorizeEndpoint</c>; without it,
    /// the issued token's <c>aud</c> claim would be whatever the client requested, and
    /// Civiti.Mcp's validator (<c>options.AddAudiences(Audience)</c>) would reject every
    /// token. If you ever drop or alter that pin, you MUST also restore both
    /// <c>ValidateResources</c> handlers and register the accepted URLs on each scope's
    /// <c>Resources</c> collection.
    /// </para>
    /// </summary>
    public const string Audience = "civiti-mcp";
}
