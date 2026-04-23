using Civiti.Domain.Entities;

namespace Civiti.Application.Requests.Issues;

public class GetUserIssuesRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public IssueStatus? Status { get; set; }
    public string SortBy { get; set; } = "date";
    public bool SortDescending { get; set; } = true;
}