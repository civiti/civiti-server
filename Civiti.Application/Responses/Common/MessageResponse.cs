namespace Civiti.Application.Responses.Common;

/// <summary>
/// Simple response containing a message string, used for success/error confirmations
/// </summary>
public class MessageResponse
{
    /// <summary>
    /// The result message
    /// </summary>
    /// <example>Operation completed successfully</example>
    public string Message { get; set; } = string.Empty;
}
