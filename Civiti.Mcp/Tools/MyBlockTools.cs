using System.ComponentModel;
using Civiti.Application.Services;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.2 write tools for the citizen's block list. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyBlockTools(IBlockService blocks, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "block_user")]
    [Description("Block another Civiti user. Their content (issues, comments) becomes hidden from the authenticated user platform-wide. Mirrors POST /api/users/me/blocks. Requires civiti.write scope.")]
    public async Task<object> BlockUser(
        [Description("Identifier (Guid) of the user to block — Civiti's internal UserProfile.Id.")] Guid blockedUserId,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenWriteAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        var (success, data, error) = await blocks.BlockUserAsync(blockedUserId, auth.Context.SupabaseUserId);
        if (!success)
        {
            return new { ok = false, reason = "service_error", message = error ?? "Block operation failed." };
        }
        // BlockUserAsync's contract guarantees non-null data when success is true; previous
        // `?? new { ok = true, ... }` fallback was dead code. Wrap in the same ok-envelope
        // shape the other confirmation-style tools (vote_on_issue, vote_on_comment,
        // report_content, unblock_user) use so the agent gets a consistent success
        // contract — block payload (BlockedUserId + BlockedAt) carried under `data`.
        return new { ok = true, blockedUserId, data };
    }

    [McpServerTool(Name = "unblock_user")]
    [Description("Remove a previously-blocked user from the authenticated user's block list. Mirrors DELETE /api/users/me/blocks/{userId}. Requires civiti.write scope.")]
    public async Task<object> UnblockUser(
        [Description("Identifier (Guid) of the user to unblock — Civiti's internal UserProfile.Id.")] Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenWriteAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        var (success, error) = await blocks.UnblockUserAsync(targetUserId, auth.Context.SupabaseUserId);
        if (!success)
        {
            return new { ok = false, reason = "service_error", message = error ?? "Unblock operation failed." };
        }
        return new { ok = true, targetUserId };
    }
}
