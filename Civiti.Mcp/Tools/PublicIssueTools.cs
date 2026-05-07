using System.ComponentModel;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Issues;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §1 Public issue tools — anonymous reads of civic data, plus the email-sent counter bump.
/// See Civiti.Mcp/docs/tool-inventory.md §1.
///
/// On the authenticated <c>/mcp</c> mount, <c>search_issues</c> and <c>get_issue</c>
/// resolve the caller's internal <c>UserProfile.Id</c> via
/// <see cref="IMcpCitizenContext.TryResolveCitizenAsync"/> and forward it to
/// <see cref="IIssueService"/>. That enables (a) the service-layer block-list filter to drop
/// content from users the caller has blocked, and (b) the <c>HasVoted</c> enrichment on
/// returned issues. On <c>/mcp/public</c> the helper returns <c>null</c> and the calls
/// behave exactly as they did before this change — the public-tier data set is unchanged.
/// </summary>
[McpServerToolType]
public sealed class PublicIssueTools(
    IIssueService issues,
    IHttpContextAccessor httpContextAccessor,
    IMcpCitizenContext citizenContext)
{
    [McpServerTool(Name = "search_issues")]
    [Description("Search reported civic issues. Returns a paged list of active, publicly visible issues. Mirrors GET /api/issues on the Civiti REST API. On the authenticated mount, results exclude content from users the caller has blocked and include HasVoted on each returned issue.")]
    public async Task<object> SearchIssues(
        [Description("Page number, 1-based. Default 1.")] int? page = null,
        [Description("Items per page, 1–100. Default 12.")] int? pageSize = null,
        [Description("Optional category filter. One of: Infrastructure, Environment, Transportation, PublicServices, Safety, Other.")] string? category = null,
        [Description("Optional urgency filter. One of: Low, Medium, High, Urgent.")] string? urgency = null,
        [Description("Optional comma-separated IssueStatus list. Default: Active. Only publicly visible statuses are returned regardless.")] string? status = null,
        [Description("Optional district filter (e.g. \"Sector 1\").")] string? district = null,
        [Description("Optional free-text address filter.")] string? address = null,
        [Description("Sort key: date | emails | votes | urgency. Default date.")] string? sortBy = null,
        [Description("Sort descending. Default true.")] bool? sortDescending = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetIssuesRequest
        {
            Page = page is > 0 ? page.Value : 1,
            PageSize = Math.Clamp(pageSize ?? 12, 1, 100),
            District = district,
            Address = address,
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "date" : sortBy,
            SortDescending = sortDescending ?? true
        };

        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<IssueCategory>(category, ignoreCase: true, out var parsedCategory))
        {
            request.Category = parsedCategory;
        }

        if (!string.IsNullOrWhiteSpace(urgency) && Enum.TryParse<UrgencyLevel>(urgency, ignoreCase: true, out var parsedUrgency))
        {
            request.Urgency = parsedUrgency;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = new List<IssueStatus>();
            foreach (var raw in status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<IssueStatus>(raw, ignoreCase: true, out var s))
                {
                    parsed.Add(s);
                }
            }
            if (parsed.Count > 0)
            {
                request.Statuses = parsed;
            }
        }

        var currentUserId = await citizenContext.TryResolveCitizenAsync(cancellationToken);
        var result = await issues.GetAllIssuesAsync(request, currentUserId);
        return new
        {
            items = result.Items,
            page = result.Page,
            pageSize = result.PageSize,
            totalItems = result.TotalItems,
            totalPages = result.TotalPages
        };
    }

    [McpServerTool(Name = "get_issue")]
    [Description("Get full details for a single issue by id. Returns null if the issue is not found, not publicly visible, or (on the authenticated mount) the issue's author is on the caller's block list.")]
    public async Task<IssueDetailResponse?> GetIssue(
        [Description("Issue id (uuid).")] Guid id,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = await citizenContext.TryResolveCitizenAsync(cancellationToken);
        return await issues.GetIssueByIdAsync(id, currentUserId);
    }

    [McpServerTool(Name = "mark_email_sent")]
    [Description("Increment the public petition-email counter for an issue. The petition itself is sent by the citizen from their own inbox; this tool only records that one was sent. Rate-limited to 1 per IP per issue per hour on the service side.")]
    public async Task<object> MarkEmailSent(
        [Description("Issue id (uuid).")] Guid id)
    {
        var clientIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(clientIp))
        {
            return new { ok = false, reason = "ip_unresolvable" };
        }

        var (success, error) = await issues.IncrementEmailCountAsync(id, clientIp);
        return success
            ? new { ok = true }
            : new { ok = false, reason = error ?? "unknown_error" };
    }
}
