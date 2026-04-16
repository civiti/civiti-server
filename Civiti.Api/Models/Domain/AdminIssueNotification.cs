namespace Civiti.Api.Models.Domain;

/// <summary>
/// Audit record of an admin notification enqueued for a new issue.
/// Primary key is the composite (IssueId, AdminEmail) which gives us
/// per-recipient idempotency: if the notification pipeline retries, we can
/// INSERT ... ON CONFLICT DO NOTHING to avoid re-sending to the same admin.
/// </summary>
public class AdminIssueNotification
{
    public Guid IssueId { get; set; }

    /// <summary>
    /// Admin email, normalized to lowercase at write time.
    /// </summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// When the email was enqueued for delivery to the admin. This does not
    /// imply successful delivery — only that we've dispatched it and therefore
    /// should not re-dispatch on retry.
    /// </summary>
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
}
