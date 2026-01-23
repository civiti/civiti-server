namespace Civica.Api.Models.Responses.Comments;

/// <summary>
/// Full comment response with user information and vote status
/// </summary>
public class CommentResponse
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Parent comment ID for replies. Null for top-level comments.
    /// </summary>
    public Guid? ParentCommentId { get; set; }

    /// <summary>
    /// Number of direct replies to this comment
    /// </summary>
    public int ReplyCount { get; set; }

    /// <summary>
    /// User information for the comment author
    /// </summary>
    public CommentUserResponse User { get; set; } = null!;

    /// <summary>
    /// Whether the current user has voted this comment as helpful
    /// </summary>
    public bool HasVoted { get; set; }
}
