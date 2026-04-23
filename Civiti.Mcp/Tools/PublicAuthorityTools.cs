using System.ComponentModel;
using Civiti.Application.Services;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §1 Public authority directory tool. See Civiti.Mcp/docs/tool-inventory.md §1.
/// </summary>
[McpServerToolType]
public sealed class PublicAuthorityTools(IAuthorityService authorities)
{
    [McpServerTool(Name = "list_authorities")]
    [Description("List active local authorities. Filter by city and/or district. Mirrors GET /api/authorities.")]
    public async Task<object> ListAuthorities(
        [Description("Optional city filter (e.g. \"București\").")] string? city = null,
        [Description("Optional district filter (e.g. \"Sector 1\").")] string? district = null)
    {
        var result = await authorities.GetActiveAuthoritiesAsync(city, district, search: null);
        return new { items = result };
    }
}
