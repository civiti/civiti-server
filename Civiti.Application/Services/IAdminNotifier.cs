namespace Civiti.Application.Services;

/// <summary>
/// Fire-and-forget entry point for admin notifications. Callers on the request
/// path should invoke this and not await any network or DB work — the notifier
/// only enqueues an in-process message; actual delivery happens in the
/// AdminNotifyBackgroundService.
/// </summary>
public interface IAdminNotifier
{
    /// <summary>
    /// Enqueue an "admin, a new issue was submitted" email blast for the given issue.
    /// Returns immediately. Does not throw on transport failures — errors are logged.
    /// </summary>
    Task NotifyNewIssueAsync(Guid issueId, CancellationToken cancellationToken = default);
}
