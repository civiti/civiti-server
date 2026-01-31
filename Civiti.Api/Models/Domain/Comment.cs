namespace Civiti.Api.Models.Domain;

/// <summary>
/// Represents a user comment on an issue
/// </summary>
public class Comment
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int HelpfulCount { get; set; } = 0;
    public bool IsEdited { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public Guid? DeletedByUserId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
    public UserProfile? DeletedByUser { get; set; }
    public Comment? ParentComment { get; set; }
    public List<Comment> Replies { get; set; } = [];
    public List<CommentVote> Votes { get; set; } = [];
}
