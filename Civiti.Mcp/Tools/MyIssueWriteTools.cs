using System.ComponentModel;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Domain.Exceptions;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.2 write tools for issues. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyIssueWriteTools(IIssueService issues, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "create_issue")]
    [Description("Create a new civic issue authored by the authenticated user. Mirrors POST /api/issues. Title (≤200), description (≤2000), category (Infrastructure | Environment | Transportation | PublicServices | Safety | Other), address (≤500), district (≤50), latitude / longitude required. Optional urgency, desiredOutcome, communityImpact. Requires civiti.write scope.")]
    public async Task<object> CreateIssue(
        [Description("Brief, descriptive title (max 200 characters).")] string title,
        [Description("Detailed description of the issue (max 2000 characters).")] string description,
        [Description("Category: Infrastructure | Environment | Transportation | PublicServices | Safety | Other.")] IssueCategory category,
        [Description("Street address or location description (max 500 characters).")] string address,
        [Description("District or sector name (max 50 characters).")] string district,
        [Description("GPS latitude, -90 to 90.")] double latitude,
        [Description("GPS longitude, -180 to 180.")] double longitude,
        [Description("Urgency: Unspecified | Low | Medium | High | Urgent. Default Medium.")] UrgencyLevel urgency = UrgencyLevel.Medium,
        [Description("Optional: desired outcome or solution (max 1000 characters).")] string? desiredOutcome = null,
        [Description("Optional: community impact narrative (max 1000 characters).")] string? communityImpact = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenWriteAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        var request = new CreateIssueRequest
        {
            Title = title,
            Description = description,
            Category = category,
            Address = address,
            District = district,
            Latitude = latitude,
            Longitude = longitude,
            Urgency = urgency,
            DesiredOutcome = desiredOutcome,
            CommunityImpact = communityImpact
        };
        try
        {
            return await issues.CreateIssueAsync(request, auth.Context.SupabaseUserId);
        }
        catch (ContentModerationException ex)
        {
            // Typed exception — only fires when the OpenAI moderation gate blocks one of the
            // free-text fields (Title / Description / Address / District / DesiredOutcome /
            // CommunityImpact). InvalidOperationException family (user not found, account
            // deleted, etc.) is left to bubble so the MCP framework returns a transport-level
            // error rather than a misleading {reason: "moderation_rejected"} payload.
            return new { ok = false, reason = "moderation_rejected", message = ex.Message };
        }
    }

    [McpServerTool(Name = "vote_on_issue")]
    [Description("Cast or remove the authenticated user's supporter vote on an issue. direction='up' adds the vote (mirrors POST /api/issues/{id}/vote); direction='remove' removes it (mirrors DELETE /api/issues/{id}/vote). Requires civiti.write scope.")]
    public async Task<object> VoteOnIssue(
        [Description("Issue identifier (Guid).")] Guid issueId,
        [Description("'up' to vote in support, 'remove' to retract a previously-cast vote.")] string direction,
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
            ? await issues.VoteForIssueAsync(issueId, auth.Context.SupabaseUserId)
            : await issues.RemoveVoteAsync(issueId, auth.Context.SupabaseUserId);
        if (!success)
        {
            return new { ok = false, reason = "service_error", message = error ?? "Vote operation failed." };
        }
        return new { ok = true, issueId, direction = direction.ToLowerInvariant() };
    }
}
