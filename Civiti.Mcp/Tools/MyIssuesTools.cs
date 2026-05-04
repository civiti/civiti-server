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
    // Whitelist mirrors the cases IssueService.GetUserIssuesAsync recognises: "status",
    // "emails", and a default that falls through to CreatedAt sort (we expose that as "date").
    // Rejecting unknown values at the tool boundary stops a typo from silently sorting
    // by date when the agent asked for something specific.
    private static readonly HashSet<string> AllowedSortByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "status", "emails"
    };

    [McpServerTool(Name = "list_my_issues")]
    [Description("Return a paged list of issues the authenticated user created. Supports filtering by IssueStatus (Draft / Submitted / UnderReview / Active / Resolved / Rejected / Cancelled) and sorting by date, status, or email count. Mirrors GET /api/users/me/issues. Requires civiti.read scope.")]
    public async Task<object> ListMyIssues(
        [Description("Optional status filter: Draft | Submitted | UnderReview | Active | Resolved | Rejected | Cancelled. Omit for all statuses.")] IssueStatus? status = null,
        [Description("Page number, 1-indexed. Default 1.")] int? page = null,
        [Description("Page size, 1–100. Default 10.")] int? pageSize = null,
        [Description("Sort field: 'date' (CreatedAt), 'status' (IssueStatus), or 'emails' (EmailsSent count). Default 'date'.")] string? sortBy = null,
        [Description("Sort direction: true = newest / highest first. Default true.")] bool? sortDescending = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenReadAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        var resolvedSortBy = string.IsNullOrWhiteSpace(sortBy) ? "date" : sortBy;
        if (!AllowedSortByValues.Contains(resolvedSortBy))
        {
            return new
            {
                ok = false,
                reason = "invalid_input",
                message = $"sortBy must be one of: {string.Join(", ", AllowedSortByValues)}."
            };
        }

        var request = new GetUserIssuesRequest
        {
            Page = page is > 0 ? page.Value : 1,
            PageSize = Math.Clamp(pageSize ?? 10, 1, 100),
            Status = status,
            SortBy = resolvedSortBy,
            SortDescending = sortDescending ?? true
        };
        return await issues.GetUserIssuesAsync(auth.Context.SupabaseUserId, request);
    }
}
