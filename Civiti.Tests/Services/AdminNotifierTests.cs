using System.Threading.Channels;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Application.Notifications;
using Civiti.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class AdminNotifierTests
{
    [Fact]
    public async Task NotifyNewIssueAsync_Should_Enqueue_When_Enabled()
    {
        var channel = Channel.CreateBounded<AdminNotifyRequest>(10);
        var config = new AdminNotifyConfiguration { Enabled = true };
        var logger = new Mock<ILogger<AdminNotifier>>();
        var notifier = new AdminNotifier(channel.Writer, config, logger.Object);
        var issueId = Guid.NewGuid();

        await notifier.NotifyNewIssueAsync(issueId);

        channel.Reader.TryRead(out AdminNotifyRequest? msg).Should().BeTrue();
        msg!.IssueId.Should().Be(issueId);
        msg.Type.Should().Be(AdminNotifyEventType.NewIssueSubmitted);
    }

    [Fact]
    public async Task NotifyNewIssueAsync_Should_NoOp_When_Disabled()
    {
        var channel = Channel.CreateBounded<AdminNotifyRequest>(10);
        var config = new AdminNotifyConfiguration { Enabled = false };
        var logger = new Mock<ILogger<AdminNotifier>>();
        var notifier = new AdminNotifier(channel.Writer, config, logger.Object);

        await notifier.NotifyNewIssueAsync(Guid.NewGuid());

        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task NotifyNewIssueAsync_Should_Not_Throw_When_Channel_Is_Full()
    {
        // Bounded capacity = 1, Wait mode: TryWrite returns false on overflow (non-blocking)
        // — the notifier must log and continue, never throw, so issue creation is unaffected.
        var channel = Channel.CreateBounded<AdminNotifyRequest>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
        var config = new AdminNotifyConfiguration { Enabled = true };
        var logger = new Mock<ILogger<AdminNotifier>>();
        var notifier = new AdminNotifier(channel.Writer, config, logger.Object);

        // First write fills the channel
        await notifier.NotifyNewIssueAsync(Guid.NewGuid());

        // Second write must silently drop without throwing
        Func<Task> act = async () => await notifier.NotifyNewIssueAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();

        // Only the first message is in the channel
        channel.Reader.TryRead(out _).Should().BeTrue();
        channel.Reader.TryRead(out _).Should().BeFalse();
    }
}
