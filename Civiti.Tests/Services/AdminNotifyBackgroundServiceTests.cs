using System.Threading.Channels;
using Civiti.Infrastructure.Data;
using Civiti.Infrastructure.Configuration;
using Civiti.Domain.Entities;
using Civiti.Application.Email.Models;
using Civiti.Application.Notifications;
using Civiti.Infrastructure.Services;
using Civiti.Infrastructure.Services.AdminNotify;
using Civiti.Application.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class AdminNotifyBackgroundServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<AdminNotifyBackgroundService>> _logger = new();
    private readonly Channel<EmailNotification> _emailChannel = Channel.CreateUnbounded<EmailNotification>();
    private readonly Mock<IEmailTemplateService> _templateService = new();
    private readonly Mock<ISupabaseAdminClient> _adminClient = new();
    private readonly ResendConfiguration _resendConfig = new() { FrontendBaseUrl = "http://localhost:4200" };

    public AdminNotifyBackgroundServiceTests()
    {
        _templateService
            .Setup(t => t.Render(It.IsAny<EmailNotificationType>(), It.IsAny<Dictionary<string, string>>()))
            .Returns<EmailNotificationType, Dictionary<string, string>>((_, data) =>
            (
                $"Nouă problemă raportată: {data.GetValueOrDefault("IssueTitle", "")}",
                $"<p>Issue: {data.GetValueOrDefault("IssueTitle", "")}</p>"
            ));
    }

    public void Dispose() => _dbFactory.Dispose();

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _dbFactory.CreateContext());
        services.AddSingleton(_adminClient.Object);
        services.AddSingleton(_templateService.Object);
        services.AddSingleton(_emailChannel.Writer);
        services.AddSingleton(_resendConfig);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private (Guid issueId, UserProfile user) SeedIssue()
    {
        using CivitiDbContext db = _dbFactory.CreateContext();
        UserProfile user = TestDataBuilder.CreateUser(displayName: "Ion Popescu");
        Issue issue = TestDataBuilder.CreateIssue(
            userId: user.Id,
            status: IssueStatus.Submitted,
            category: IssueCategory.Infrastructure);
        db.UserProfiles.Add(user);
        db.Issues.Add(issue);
        db.SaveChanges();
        return (issue.Id, user);
    }

    /// <summary>
    /// Runs the service on the current channel contents and returns when processing completes.
    /// Writes to the channel are done BEFORE calling this, then the channel is completed so
    /// the service's <c>ReadAllAsync</c> loop exits naturally.
    /// </summary>
    private async Task RunAsync(Channel<AdminNotifyRequest> channel)
    {
        channel.Writer.Complete();

        var service = new AdminNotifyBackgroundService(channel.Reader, CreateScopeFactory(), _logger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);

        service.ExecuteTask.Should().NotBeNull();
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Should_Enqueue_One_Email_Per_Admin_With_Deep_Link()
    {
        (Guid issueId, _) = SeedIssue();
        _adminClient.Setup(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SupabaseAdminUser(Guid.NewGuid(), "admin1@example.com"),
                new SupabaseAdminUser(Guid.NewGuid(), "admin2@example.com")
            });

        var channel = Channel.CreateUnbounded<AdminNotifyRequest>();
        await channel.Writer.WriteAsync(new AdminNotifyRequest(issueId, AdminNotifyEventType.NewIssueSubmitted));

        await RunAsync(channel);

        var sent = new List<EmailNotification>();
        while (_emailChannel.Reader.TryRead(out EmailNotification? msg)) sent.Add(msg);

        sent.Should().HaveCount(2);
        sent.Select(s => s.To).Should().BeEquivalentTo(["admin1@example.com", "admin2@example.com"]);
        sent.Should().OnlyContain(s => s.Type == EmailNotificationType.AdminNewIssue);

        // Template received a CtaUrl pointing at /admin/issues/{id}
        _templateService.Verify(t => t.Render(
            EmailNotificationType.AdminNewIssue,
            It.Is<Dictionary<string, string>>(d =>
                d.ContainsKey("CtaUrl") && d["CtaUrl"] == $"http://localhost:4200/admin/issues/{issueId}"
                && d.ContainsKey("IssueTitle"))), Times.Once);
    }

    [Fact]
    public async Task Should_Skip_Already_Notified_Admins()
    {
        (Guid issueId, _) = SeedIssue();
        var alreadyNotifiedEmail = "alice@example.com";
        var newAdminEmail = "bob@example.com";

        // Pre-insert an audit row for alice — a previous dispatch attempt.
        using (CivitiDbContext db = _dbFactory.CreateContext())
        {
            db.AdminIssueNotifications.Add(new AdminIssueNotification
            {
                IssueId = issueId,
                AdminEmail = alreadyNotifiedEmail,
                EnqueuedAt = DateTime.UtcNow.AddMinutes(-1)
            });
            db.SaveChanges();
        }

        _adminClient.Setup(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SupabaseAdminUser(Guid.NewGuid(), alreadyNotifiedEmail),
                new SupabaseAdminUser(Guid.NewGuid(), newAdminEmail)
            });

        var channel = Channel.CreateUnbounded<AdminNotifyRequest>();
        await channel.Writer.WriteAsync(new AdminNotifyRequest(issueId, AdminNotifyEventType.NewIssueSubmitted));

        await RunAsync(channel);

        var sent = new List<EmailNotification>();
        while (_emailChannel.Reader.TryRead(out EmailNotification? msg)) sent.Add(msg);

        sent.Should().ContainSingle().Which.To.Should().Be(newAdminEmail);
    }

    [Fact]
    public async Task Should_Normalize_Admin_Emails_To_Lowercase()
    {
        (Guid issueId, _) = SeedIssue();
        _adminClient.Setup(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()))
            // Supabase admin client itself normalizes, but be defensive in case it doesn't
            .ReturnsAsync(new[] { new SupabaseAdminUser(Guid.NewGuid(), "Alice@Example.COM") });

        var channel = Channel.CreateUnbounded<AdminNotifyRequest>();
        await channel.Writer.WriteAsync(new AdminNotifyRequest(issueId, AdminNotifyEventType.NewIssueSubmitted));

        await RunAsync(channel);

        var sent = new List<EmailNotification>();
        while (_emailChannel.Reader.TryRead(out EmailNotification? msg)) sent.Add(msg);
        sent.Should().ContainSingle().Which.To.Should().Be("alice@example.com");

        // Audit row uses normalized email too.
        using CivitiDbContext db = _dbFactory.CreateContext();
        db.AdminIssueNotifications.Should().ContainSingle(n => n.AdminEmail == "alice@example.com");
    }

    [Fact]
    public async Task Should_Skip_When_No_Admins_Exist()
    {
        (Guid issueId, _) = SeedIssue();
        _adminClient.Setup(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SupabaseAdminUser>());

        var channel = Channel.CreateUnbounded<AdminNotifyRequest>();
        await channel.Writer.WriteAsync(new AdminNotifyRequest(issueId, AdminNotifyEventType.NewIssueSubmitted));

        await RunAsync(channel);

        _emailChannel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Should_Skip_When_Issue_Missing()
    {
        _adminClient.Setup(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SupabaseAdminUser(Guid.NewGuid(), "admin@example.com") });

        var channel = Channel.CreateUnbounded<AdminNotifyRequest>();
        await channel.Writer.WriteAsync(new AdminNotifyRequest(Guid.NewGuid(), AdminNotifyEventType.NewIssueSubmitted));

        await RunAsync(channel);

        _emailChannel.Reader.TryRead(out _).Should().BeFalse();
        _adminClient.Verify(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Not_Persist_Audit_When_Email_Channel_Is_Full()
    {
        // Regression guard: if the email channel is full, we must NOT write the audit
        // row — otherwise a future retry sees "already notified" and the admin is
        // permanently silenced. Enqueue first, persist after success.
        //
        // FullMode.Wait is required: it makes TryWrite return false on overflow.
        // (DropWrite would silently drop the message AND return true, making the failure
        // undetectable — which is why Program.cs was switched to Wait in this PR.)
        var fullChannel = Channel.CreateBounded<EmailNotification>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
        // Pre-fill to capacity; the next TryWrite will return false.
        fullChannel.Writer.TryWrite(new EmailNotification(
            "placeholder@example.com", "placeholder", "<p/>", EmailNotificationType.Welcome))
            .Should().BeTrue();

        // Rebuild the scope factory to hand out this pre-filled channel writer
        var services = new ServiceCollection();
        services.AddScoped(_ => _dbFactory.CreateContext());
        services.AddSingleton(_adminClient.Object);
        services.AddSingleton(_templateService.Object);
        services.AddSingleton(fullChannel.Writer);
        services.AddSingleton(_resendConfig);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        (Guid issueId, _) = SeedIssue();
        _adminClient.Setup(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SupabaseAdminUser(Guid.NewGuid(), "admin@example.com") });

        var channel = Channel.CreateUnbounded<AdminNotifyRequest>();
        await channel.Writer.WriteAsync(new AdminNotifyRequest(issueId, AdminNotifyEventType.NewIssueSubmitted));
        channel.Writer.Complete();

        var service = new AdminNotifyBackgroundService(channel.Reader, scopeFactory, _logger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);
        service.ExecuteTask.Should().NotBeNull();
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        // No audit row was written (so a retry can try again later).
        using CivitiDbContext db = _dbFactory.CreateContext();
        db.AdminIssueNotifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_Not_Throw_When_Supabase_Client_Fails()
    {
        (Guid issueId, _) = SeedIssue();
        _adminClient.Setup(c => c.ListAdminsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var channel = Channel.CreateUnbounded<AdminNotifyRequest>();
        await channel.Writer.WriteAsync(new AdminNotifyRequest(issueId, AdminNotifyEventType.NewIssueSubmitted));

        Func<Task> act = () => RunAsync(channel);
        await act.Should().NotThrowAsync();
        _emailChannel.Reader.TryRead(out _).Should().BeFalse();
    }
}
