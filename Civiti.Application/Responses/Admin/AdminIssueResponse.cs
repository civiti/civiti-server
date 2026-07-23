using Civiti.Domain.Entities;

namespace Civiti.Application.Responses.Admin;

/// <summary>
/// Lightweight response for admin issue list views
/// </summary>
public class AdminIssueResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public IssueStatus Status { get; set; }
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int PhotoCount { get; set; }
    public int EmailsSent { get; set; }

    /// <summary>
    /// True when this queue item is an edit of content that was already approved once, rather
    /// than a first-time submission.
    /// <para>
    /// These are the items that must not be waved through: they keep the supporter counters
    /// they earned under their previous content, so approving one without reading the
    /// field-level diff on the detail screen is how an endorsed issue gets swapped for
    /// something else. Bulk approval in particular shows a reviewer nothing else that
    /// distinguishes them.
    /// </para>
    /// </summary>
    public bool IsReReview { get; set; }

    // Minimal user info
    public string UserName { get; set; } = string.Empty;
}
