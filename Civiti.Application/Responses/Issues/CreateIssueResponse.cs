namespace Civiti.Application.Responses.Issues;

/// <summary>
/// Response returned after successfully creating a new issue
/// </summary>
public class CreateIssueResponse
{
    /// <summary>
    /// The unique identifier of the newly created issue
    /// </summary>
    /// <example>a1b2c3d4-e5f6-7890-abcd-ef1234567890</example>
    public Guid Id { get; set; }

    /// <summary>
    /// The initial status of the issue (typically "Submitted" pending admin review)
    /// </summary>
    /// <example>Submitted</example>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the issue was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
