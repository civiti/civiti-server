using System.ComponentModel;
using Civiti.Application.Requests.Comments;
using Civiti.Application.Services;
using Civiti.Domain.Exceptions;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.2 write tools for comments. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyCommentTools(ICommentService comments, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "add_comment")]
    [Description("Add a comment to an issue, optionally as a reply to an existing comment (parentCommentId). Content runs through OpenAI moderation; rejections surface as {ok: false, reason: 'moderation_rejected'}. Mirrors POST /api/issues/{issueId}/comments. Requires civiti.write scope.")]
    public async Task<object> AddComment(
        [Description("Target issue identifier (Guid).")] Guid issueId,
        [Description("Comment text, 1–2000 characters.")] string content,
        [Description("Optional parent comment id for replies; omit for a top-level comment.")] Guid? parentCommentId = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenWriteAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        var request = new CreateCommentRequest
        {
            Content = content,
            ParentCommentId = parentCommentId
        };
        try
        {
            return await comments.CreateCommentAsync(issueId, request, auth.Context.SupabaseUserId);
        }
        catch (ContentModerationException ex)
        {
            // Typed exception — only fires when the OpenAI moderation gate blocks the content,
            // never for the unrelated state errors (issue not found, parent on different
            // issue, rate-limited, duplicate, etc.) that CommentService surfaces as plain
            // InvalidOperationException. Those are left to bubble so the MCP framework
            // returns a proper transport-level error rather than a misleading
            // {reason: "moderation_rejected"} payload.
            return new { ok = false, reason = "moderation_rejected", message = ex.Message };
        }
    }

    [McpServerTool(Name = "vote_on_comment")]
    [Description("Mark a comment as helpful or remove a previously-cast helpful vote. direction='up' marks helpful; direction='remove' retracts. Requires civiti.write scope.")]
    public async Task<object> VoteOnComment(
        [Description("Comment identifier (Guid).")] Guid commentId,
        [Description("'up' to mark helpful, 'remove' to retract.")] string direction,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenWriteAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        if (string.IsNullOrWhiteSpace(direction) || !ToolInputVocabularies.VoteDirections.Contains(direction))
        {
            return new { ok = false, reason = "invalid_input", message = $"direction must be one of: {string.Join(", ", ToolInputVocabularies.VoteDirections)}." };
        }

        var (success, error) = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase)
            ? await comments.VoteHelpfulAsync(commentId, auth.Context.SupabaseUserId)
            : await comments.RemoveVoteAsync(commentId, auth.Context.SupabaseUserId);
        if (!success)
        {
            return new { ok = false, reason = "service_error", message = error ?? "Vote operation failed." };
        }
        return new { ok = true, commentId, direction = direction.ToLowerInvariant() };
    }
}
