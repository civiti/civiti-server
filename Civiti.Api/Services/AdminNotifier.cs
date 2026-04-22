using System.Threading.Channels;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Application.Notifications;
using Civiti.Application.Services;

namespace Civiti.Api.Services;

/// <summary>
/// Default <see cref="IAdminNotifier"/> — a thin producer that drops a message on the
/// admin-notify channel and returns. The AdminNotifyBackgroundService does the real
/// work (Supabase lookup, fanout, email render + enqueue).
/// </summary>
public sealed class AdminNotifier(
    ChannelWriter<AdminNotifyRequest> channelWriter,
    AdminNotifyConfiguration config,
    ILogger<AdminNotifier> logger) : IAdminNotifier
{
    public Task NotifyNewIssueAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        if (!config.Enabled)
        {
            logger.LogDebug("Admin notification disabled (AdminNotify:Enabled=false) — skipping issue {IssueId}", issueId);
            return Task.CompletedTask;
        }

        AdminNotifyRequest request = new(issueId, AdminNotifyEventType.NewIssueSubmitted);

        if (!channelWriter.TryWrite(request))
        {
            logger.LogError(
                "Admin-notify channel full — dropped notification for issue {IssueId}. "
                + "Increase AdminNotify:ChannelCapacity if this persists.",
                issueId);
        }

        return Task.CompletedTask;
    }
}
