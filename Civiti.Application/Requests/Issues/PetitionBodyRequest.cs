using Civiti.Domain.Entities;

namespace Civiti.Application.Requests.Issues;

/// <summary>
/// Input for AI petition-body generation. Assembled server-side from a stored issue —
/// not bound directly from an untrusted HTTP body — so it carries no validation attributes.
/// </summary>
public class PetitionBodyRequest
{
    /// <summary>The issue the petition is about (used for the documentation link).</summary>
    public Guid IssueId { get; set; }

    /// <summary>Issue title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Issue category (adds context for the AI-composed core).</summary>
    public IssueCategory Category { get; set; }

    /// <summary>Street address / location description.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Optional district / sector.</summary>
    public string? District { get; set; }

    /// <summary>The citizen's problem description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional desired outcome.</summary>
    public string? DesiredOutcome { get; set; }

    /// <summary>Optional community-impact narrative.</summary>
    public string? CommunityImpact { get; set; }

    /// <summary>Number of attached photos (drives the "anexez N fotografii" line).</summary>
    public int PhotoCount { get; set; }

    /// <summary>When the issue was created (rendered as "Data sesizării").</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When true, ask the model for a fresh, differently-worded variation.</summary>
    public bool Regenerate { get; set; }
}

/// <summary>
/// HTTP request body for <c>POST /api/issues/{id}/petition-body</c>. All fields optional.
/// </summary>
/// <param name="Regenerate">Set true to bypass any cache and produce a new variation.</param>
public sealed record PetitionBodyOptions(bool Regenerate = false);
