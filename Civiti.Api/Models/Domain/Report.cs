namespace Civiti.Api.Models.Domain;

public class Report
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public ReportReason Reason { get; set; }
    public string? Details { get; set; }
    // TODO: Status transitions (Reviewed / Dismissed) are not yet implemented.
    // Add an admin endpoint and service method before exposing this field to consumers.
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public UserProfile Reporter { get; set; } = null!;
}

public enum ReportReason
{
    Spam = 0,
    Harassment = 1,
    Inappropriate = 2,
    Misinformation = 3,
    Other = 4
}

public enum ReportStatus
{
    Pending = 0,
    Reviewed = 1,
    Dismissed = 2
}
