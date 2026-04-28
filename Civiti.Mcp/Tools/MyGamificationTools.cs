using System.ComponentModel;
using Civiti.Application.Services;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.1 read tool. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyGamificationTools(IUserService users, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "get_my_gamification")]
    [Description("Return the authenticated user's gamification view: total points, current level, badges earned, achievement progress, and login streak. Mirrors GET /api/users/me/gamification. Requires civiti.read scope.")]
    public async Task<object> GetMyGamification(CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenReadAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload!;
        }

        var gamification = await users.GetUserGamificationAsync(auth.Context.SupabaseUserId);
        if (gamification is null)
        {
            return new { ok = false, reason = "user_profile_missing", message = "No Civiti user profile is linked to this account yet — finish signup in the app first." };
        }
        return gamification;
    }
}
