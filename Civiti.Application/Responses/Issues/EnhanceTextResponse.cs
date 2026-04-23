namespace Civiti.Application.Responses.Issues;

/// <summary>
/// Response model for AI-enhanced text generation
/// </summary>
public class EnhanceTextResponse
{
    /// <summary>
    /// The enhanced description text
    /// </summary>
    public string EnhancedDescription { get; set; } = string.Empty;

    /// <summary>
    /// The enhanced desired outcome text, if provided in request
    /// </summary>
    public string? EnhancedDesiredOutcome { get; set; }

    /// <summary>
    /// The enhanced community impact text, if provided in request
    /// </summary>
    public string? EnhancedCommunityImpact { get; set; }

    /// <summary>
    /// Indicates if the original text was returned due to AI failure
    /// </summary>
    public bool UsedOriginalText { get; set; }

    /// <summary>
    /// Warning message if AI enhancement failed and original text was used
    /// </summary>
    public string? Warning { get; set; }

    /// <summary>
    /// Indicates if the request was rate limited
    /// </summary>
    public bool IsRateLimited { get; set; }
}
