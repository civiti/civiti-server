namespace Civiti.Domain.Entities;

/// <summary>
/// The content of an issue as an admin last approved it, kept so a later owner edit can be shown
/// to the next reviewer as a field-level diff rather than as an undifferentiated wall of text.
/// <para>
/// This is what makes editing a live issue safe. An approved issue keeps its supporter counters
/// across an edit, so without a diff an issue approved on benign content could be quietly
/// swapped for spam and keep every endorsement it had earned.
/// </para>
/// <para>
/// One row per issue in v1. The key is deliberately the issue id alone rather than a
/// <c>(IssueId, Version)</c> pair — full revision history is a larger feature, and this table is
/// shaped so it can become that by relaxing the key rather than by being rewritten.
/// </para>
/// </summary>
public class IssueApprovedSnapshot
{
    /// <summary>Primary key — one snapshot per issue.</summary>
    public Guid IssueId { get; set; }

    /// <summary>When the content in <see cref="Payload"/> was approved, or captured as approved.</summary>
    public DateTime ApprovedAt { get; set; }

    /// <summary>
    /// The approving admin, when known. Null for a snapshot captured lazily from an
    /// already-live issue whose approval predates this table.
    /// </summary>
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>Serialised <c>IssueContentSnapshot</c>.</summary>
    public string Payload { get; set; } = string.Empty;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
}
