namespace Civica.Api.Models.Domain;

public class EmailTracking
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public Guid UserId { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
    public string AuthorityEmail { get; set; } = string.Empty;
    public string? AuthorityName { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailBody { get; set; }
    public string RecipientType { get; set; } = "authority";
    public string TrackingStatus { get; set; } = "sent";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
}