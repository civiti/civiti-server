namespace Civiti.Application.Responses.Issues;

/// <summary>
/// Response model for AI petition-body generation.
/// </summary>
public class PetitionBodyResponse
{
    /// <summary>
    /// The full, ready-to-copy Romanian petition body: the legally-compliant scaffold
    /// (identity block, O.G. 27/2002 reply clause, bracketed PII placeholders, sign-off)
    /// with the AI-composed argument core inserted. Always populated — even on AI failure
    /// a deterministic core is used so the body is never empty.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// True when AI composition was unavailable and a deterministic core (built from the
    /// raw issue fields) was used instead.
    /// </summary>
    public bool UsedOriginalText { get; set; }

    /// <summary>Warning message when the deterministic fallback core was used.</summary>
    public string? Warning { get; set; }

    /// <summary>True when the request was rate limited.</summary>
    public bool IsRateLimited { get; set; }
}
