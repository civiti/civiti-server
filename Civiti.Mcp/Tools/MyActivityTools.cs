using System.ComponentModel;
using Civiti.Application.Requests.Activity;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.1 read tool. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyActivityTools(IActivityService activity, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "list_my_activity")]
    [Description("Return a paged feed of activity events on issues the authenticated user follows: status changes, approvals, new supporters, comments. Mirrors GET /api/users/me/activity. Requires civiti.read scope.")]
    public async Task<object> ListMyActivity(
        [Description("Optional activity-type filter: NewSupporters | StatusChange | IssueApproved | IssueResolved | IssueCreated | NewComment. Omit for all types.")] ActivityType? type = null,
        [Description("Page number, 1-indexed. Default 1.")] int? page = null,
        [Description("Page size, 1–100. Default 20.")] int? pageSize = null,
        [Description("ISO-8601 timestamp; only return activities after this. Omit for no lower bound.")] DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        // Activity service keys off the internal Guid (UserProfile.Id) rather than the Supabase
        // sub, so we resolve here. The DB round-trip is unavoidable; users that hit list_my_activity
        // will frequently re-hit it as the agent paginates, so a future refactor of the service
        // signature could amortise this away — out of scope for v1c PR 2.
        var auth = await citizenContext.ResolveCitizenAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload!;
        }

        var request = new GetActivitiesRequest
        {
            Page = page is > 0 ? page.Value : 1,
            PageSize = Math.Clamp(pageSize ?? 20, 1, 100),
            Type = type,
            Since = since
        };
        return await activity.GetUserActivitiesAsync(auth.Context.InternalUserId!.Value, request);
    }
}
