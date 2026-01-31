using Civiti.Api.Models.Domain;

namespace Civiti.Api.Models.Requests.Admin;

public class GetAdminActionsRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public Guid? IssueId { get; set; }
    public Guid? AdminUserId { get; set; }
    public AdminActionType? ActionType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}