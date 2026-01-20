using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Issues;

public class GetIssuesRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public IssueCategory? Category { get; set; }
    public UrgencyLevel? Urgency { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }
    public string SortBy { get; set; } = "date";
    public bool SortDescending { get; set; } = true;
}