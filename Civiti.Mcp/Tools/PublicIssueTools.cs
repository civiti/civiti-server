using System.ComponentModel;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §1 Public issue tools — anonymous reads of civic data, plus the email-sent counter bump.
/// See Civiti.Mcp/docs/tool-inventory.md §1.
/// </summary>
[McpServerToolType]
public sealed class PublicIssueTools(IIssueService issues, IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "search_issues")]
    [Description("Search reported civic issues. Returns a paged list of active, publicly visible issues. Mirrors GET /api/issues on the Civiti REST API.")]
    public async Task<object> SearchIssues(
        [Description("Page number, 1-based. Default 1.")] int? page = null,
        [Description("Items per page, 1–100. Default 12.")] int? pageSize = null,
        [Description("Optional category filter. One of: Infrastructure, Environment, Transportation, PublicServices, Safety, Other.")] string? category = null,
        [Description("Optional urgency filter. One of: Low, Medium, High, Critical.")] string? urgency = null,
        [Description("Optional comma-separated IssueStatus list. Default: Active. Only publicly visible statuses are returned regardless.")] string? status = null,
        [Description("Optional district filter (e.g. \"Sector 1\").")] string? district = null,
        [Description("Optional free-text address filter.")] string? address = null,
        [Description("Sort key: date | popularity | votes | urgency. Default date.")] string? sortBy = null,
        [Description("Sort descending. Default true.")] bool? sortDescending = null)
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

        var result = await issues.GetAllIssuesAsync(request, currentUserId: null);
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
    [Description("Get full details for a single issue by id. Returns null if the issue is not found or not publicly visible.")]
    public Task<object?> GetIssue(
        [Description("Issue id (uuid).")] Guid id)
        => issues.GetIssueByIdAsync(id, currentUserId: null).ContinueWith<object?>(t => t.Result);

    [McpServerTool(Name = "mark_email_sent")]
    [Description("Increment the public petition-email counter for an issue. The petition itself is sent by the citizen from their own inbox; this tool only records that one was sent. Rate-limited to 1 per IP per issue per hour on the service side.")]
    public async Task<object> MarkEmailSent(
        [Description("Issue id (uuid).")] Guid id)
    {
        var clientIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var (success, error) = await issues.IncrementEmailCountAsync(id, clientIp);
        return success
            ? new { ok = true }
            : new { ok = false, reason = error ?? "unknown_error" };
    }
}
