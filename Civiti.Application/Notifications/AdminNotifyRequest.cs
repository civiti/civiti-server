namespace Civiti.Application.Notifications;

/// <summary>
/// Message passed through the admin-notify channel. Currently only carries an IssueId;
/// worker re-reads the issue (and its author) from the DB to get fresh data and to
/// keep this message lightweight.
///
/// Wrapping the IssueId in a named type (instead of passing Guid directly through the
/// channel) leaves room to add other event types — e.g. issue status change — without
/// breaking the transport.
/// </summary>
public record AdminNotifyRequest(Guid IssueId, AdminNotifyEventType Type);

/// <summary>
/// Discriminator for admin notifications. Only one event type exists today,
/// but new event types (status change, new comment on flagged issue, etc.)
/// can be added without adding channels.
/// </summary>
public enum AdminNotifyEventType
{
    NewIssueSubmitted
}
