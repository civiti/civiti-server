namespace Civiti.Application.Responses.Common;

/// <summary>
/// Standard error response format
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Error message describing what went wrong
    /// </summary>
    /// <example>Invalid request parameters</example>
    public string Error { get; set; } = string.Empty;
    
    /// <summary>
    /// Detailed error information (only in development)
    /// </summary>
    public string? Detail { get; set; }
    
    /// <summary>
    /// Unique request ID for tracking
    /// </summary>
    /// <example>abc123-def456-ghi789</example>
    public string? RequestId { get; set; }
    
    /// <summary>
    /// Timestamp when the error occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// HTTP status code
    /// </summary>
    /// <example>400</example>
    public int StatusCode { get; set; }
    
    /// <summary>
    /// Validation errors if applicable
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}