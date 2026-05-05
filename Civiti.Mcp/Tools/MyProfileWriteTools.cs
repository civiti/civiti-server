using System.ComponentModel;
using Civiti.Application.Requests.Auth;
using Civiti.Application.Services;
using Civiti.Domain.Exceptions;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.2 write tool for the citizen's own profile. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyProfileWriteTools(IUserService users, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "update_my_profile")]
    [Description("Patch the authenticated user's profile. Every field is optional — omit a field to leave it unchanged. Mirrors PATCH /api/users/me. Requires civiti.write scope.")]
    public async Task<object> UpdateMyProfile(
        [Description("Updated display name (max 100 characters). Omit to keep current.")] string? displayName = null,
        [Description("Updated profile photo URL (max 500 characters, must be a valid URL). Omit to keep current.")] string? photoUrl = null,
        [Description("Updated county (max 100 characters). Omit to keep current.")] string? county = null,
        [Description("Updated city (max 100 characters). Omit to keep current.")] string? city = null,
        [Description("Updated district (max 100 characters). Omit to keep current.")] string? district = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenWriteAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        var request = new UpdateUserProfileRequest
        {
            DisplayName = displayName,
            PhotoUrl = photoUrl,
            County = county,
            City = city,
            District = district
        };
        try
        {
            return await users.UpdateUserProfileAsync(auth.Context.SupabaseUserId, request);
        }
        catch (ContentModerationException ex)
        {
            // Moderation block on DisplayName. Other validation errors (length cap,
            // PhotoUrl scheme/length) come back as InvalidOperationException and are left
            // to bubble — those map to MCP transport-level errors with the right semantics.
            return new { ok = false, reason = "moderation_rejected", message = ex.Message };
        }
    }
}
