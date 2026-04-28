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
    /// <c>principal.SetResources(...)</c> on the OpenIddict <c>ProcessSignIn</c> path.
    /// </summary>
    public const string Audience = "civiti-mcp";
}
