namespace Civica.Api.Models.Domain;

public class AdminAction
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public Guid? AdminUserId { get; set; }
    public string? AdminSupabaseId { get; set; }
    public AdminActionType ActionType { get; set; }
    public string? Notes { get; set; }
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? AssignedDepartment { get; set; }
    public string? EstimatedResolutionTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
    public UserProfile? AdminUser { get; set; }
}

public enum AdminActionType
{
    Approve,
    Reject,
    RequestChanges,
    Assign,
    Comment
}