namespace Civiti.Domain.Entities;

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

    /// <summary>
    /// The issue's own creator edited it and sent it back for re-approval. Not an admin
    /// action, but it belongs in the same history: it is the answer to "why is this back in
    /// my queue?", and the moderation timeline is where a reviewer looks for it.
    /// <see cref="AdminAction.AdminUserId"/> holds the acting owner.
    /// </summary>
    Resubmit
}