using System.ComponentModel;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.1 read tool. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyIssuesTools(IIssueService issues, IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "list_my_issues")]
    [Description("Return a paged list of issues the authenticated user created. Supports filtering by IssueStatus (Draft / Submitted / UnderReview / Active / Resolved / Rejected / Cancelled) and sorting by date or popularity. Mirrors GET /api/users/me/issues. Requires civiti.read scope.")]
    public async Task<object> ListMyIssues(
        [Description("Optional status filter: Draft | Submitted | UnderReview | Active | Resolved | Rejected | Cancelled. Omit for all statuses.")] IssueStatus? status = null,
        [Description("Page number, 1-indexed. Default 1.")] int? page = null,
        [Description("Page size, 1–100. Default 10.")] int? pageSize = null,
        [Description("Sort field: 'date' (creation time) or 'popularity' (vote count). Default 'date'.")] string? sortBy = null,
        [Description("Sort direction: true = newest/most-popular first. Default true.")] bool? sortDescending = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenReadAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload!;
        }

        var request = new GetUserIssuesRequest
        {
            Page = page is > 0 ? page.Value : 1,
            PageSize = Math.Clamp(pageSize ?? 10, 1, 100),
            Status = status,
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "date" : sortBy,
            SortDescending = sortDescending ?? true
        };
        return await issues.GetUserIssuesAsync(auth.Context.SupabaseUserId, request);
    }
}
