namespace Civica.Api.Models.Responses.Admin;

public class GetModerationStatsResponse
{
    public Guid AdminUserId { get; set; }
    public string AdminName { get; set; } = string.Empty;
    public int TotalActionsPerformed { get; set; }
    public int IssuesApproved { get; set; }
    public int IssuesRejected { get; set; }
    public int ChangesRequested { get; set; }
    public double AverageReviewTimeHours { get; set; }
    public DateTime? LastActionDate { get; set; }
    public int ActionsToday { get; set; }
    public int ActionsThisWeek { get; set; }
    public int ActionsThisMonth { get; set; }
}