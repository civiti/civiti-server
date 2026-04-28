using System.ComponentModel;
using Civiti.Application.Services;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.1 read tool. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyBlockedUsersTools(IBlockService blocks, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "list_my_blocked_users")]
    [Description("Return the list of users the authenticated user has blocked. Blocked users' content is hidden from the user across the platform. Mirrors GET /api/users/me/blocks. Requires civiti.read scope.")]
    public async Task<object> ListMyBlockedUsers(CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenReadAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload!;
        }

        var (success, data, error) = await blocks.GetBlockedUsersAsync(auth.Context.SupabaseUserId);
        if (!success)
        {
            return new { ok = false, reason = "service_error", message = error ?? "Failed to fetch blocked users." };
        }
        return data ?? new();
    }
}
