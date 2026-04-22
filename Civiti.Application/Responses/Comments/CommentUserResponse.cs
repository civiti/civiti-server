namespace Civiti.Application.Responses.Comments;

/// <summary>
/// Nested user information for comment responses
/// </summary>
public class CommentUserResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public int Level { get; set; }
}
