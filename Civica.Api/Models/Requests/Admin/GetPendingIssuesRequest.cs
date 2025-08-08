using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Admin;

public class GetPendingIssuesRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public IssueCategory? Category { get; set; }
    public UrgencyLevel? Urgency { get; set; }
    public string? SearchTerm { get; set; }
    public DateTime? SubmittedAfter { get; set; }
    public DateTime? SubmittedBefore { get; set; }
    public string? SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}