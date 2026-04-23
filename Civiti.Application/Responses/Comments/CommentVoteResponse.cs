namespace Civiti.Application.Responses.Comments;

/// <summary>
/// Response returned when voting or removing a vote on a comment
/// </summary>
public class CommentVoteResponse
{
    /// <summary>
    /// Result message describing the action taken
    /// </summary>
    /// <example>Vote recorded</example>
    public string Message { get; set; } = string.Empty;
}
