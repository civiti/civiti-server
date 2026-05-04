using System.ComponentModel;
using Civiti.Application.Services;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.1 read tool. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyProfileTools(IUserService users, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "get_my_profile")]
    [Description("Return the authenticated user's Civiti profile (display name, location, signup metadata, soft-deletion state). Mirrors GET /api/users/me. Requires civiti.read scope.")]
    public async Task<object> GetMyProfile(CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenReadAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload!;
        }

        var profile = await users.GetUserProfileAsync(auth.Context.SupabaseUserId);
        if (profile is null)
        {
            return new { ok = false, reason = "user_profile_missing", message = "No Civiti user profile is linked to this account yet — finish signup in the app first." };
        }
        return profile;
    }
}
