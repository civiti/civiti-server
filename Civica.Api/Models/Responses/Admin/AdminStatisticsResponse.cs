namespace Civica.Api.Models.Responses.Admin;

public class AdminStatisticsResponse
{
    // Issue statistics
    public int TotalSubmissions { get; set; }
    public int PendingReview { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int InProgress { get; set; }
    public int Resolved { get; set; }
    
    // Time-based statistics
    public int SubmissionsToday { get; set; }
    public int SubmissionsThisWeek { get; set; }
    public int SubmissionsThisMonth { get; set; }
    
    // Admin activity
    public int ReviewedToday { get; set; }
    public int ReviewedThisWeek { get; set; }
    public int ReviewedThisMonth { get; set; }
    public double AverageReviewTimeHours { get; set; }
    
    // Category breakdown
    public Dictionary<string, int> IssuesByCategory { get; set; } = new();
    public Dictionary<string, int> IssuesByUrgency { get; set; } = new();
    public Dictionary<string, int> IssuesByPriority { get; set; } = new();
    
    // User statistics
    public int TotalUsers { get; set; }
    public int ActiveUsersThisMonth { get; set; }
    public int TotalEmailsSent { get; set; }
    
    // Performance metrics
    public double ApprovalRate { get; set; }
    public double ResolutionRate { get; set; }
    public int BacklogCount { get; set; }
    
    // Period information
    public string Period { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}