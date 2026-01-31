namespace Civiti.Api.Models.Domain;

/// <summary>
/// Represents a user-facing activity event for issues (new supporters, status changes, approvals)
/// </summary>
public class Activity
{
    public Guid Id { get; set; }
    public ActivityType Type { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid IssueId { get; set; }
    public Guid IssueOwnerUserId { get; set; }
    public string? Metadata { get; set; }
    public string IssueTitle { get; set; } = string.Empty;
    public string? ActorDisplayName { get; set; }
    public int AggregatedCount { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
    public UserProfile? ActorUser { get; set; }
    public UserProfile IssueOwner { get; set; } = null!;
}

/// <summary>
/// Types of activity events that can be tracked
/// </summary>
public enum ActivityType
{
    /// <summary>Email sent by supporter (aggregated within time window)</summary>
    NewSupporters,
    /// <summary>Issue status was changed</summary>
    StatusChange,
    /// <summary>Issue was approved by admin</summary>
    IssueApproved,
    /// <summary>Issue was marked as resolved</summary>
    IssueResolved,
    /// <summary>New issue was created</summary>
    IssueCreated,
    /// <summary>New comment was added to an issue</summary>
    NewComment
}
