namespace Civica.Api.Models.Domain;

/// <summary>
/// Represents a user's upvote on an issue to show community support
/// </summary>
public class IssueVote
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
}
