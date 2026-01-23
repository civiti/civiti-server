namespace Civica.Api.Models.Domain;

/// <summary>
/// Represents a user's helpful vote on a comment
/// </summary>
public class CommentVote
{
    public Guid Id { get; set; }
    public Guid CommentId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Comment Comment { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
}
