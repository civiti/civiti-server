using Civiti.Domain.Entities;
using Civiti.Api.Models.Requests.Admin;
using Civiti.Api.Services;
using Civiti.Api.Services.Interfaces;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class AdminServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<AdminService>> _logger = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<IActivityService> _activityService = new();
    private readonly Mock<INotificationService> _notificationService = new();

    private AdminService CreateService()
    {
        var context = _dbFactory.CreateContext();
        return new AdminService(
            _logger.Object, context,
            _gamificationService.Object, _activityService.Object, _notificationService.Object);
    }

    public void Dispose() => _dbFactory.Dispose();

    // ── ApproveIssueAsync ──

    [Fact]
    public async Task ApproveIssue_Should_Set_Status_To_Active()
    {
        var admin = TestDataBuilder.CreateUser(displayName: "Admin");
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.ApproveIssueAsync(issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        result.Success.Should().BeTrue();
        result.NewStatus.Should().Be("Active");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.Issues.FindAsync(issue.Id);
        updated!.Status.Should().Be(IssueStatus.Active);
    }

    [Fact]
    public async Task ApproveIssue_Should_Award_50_Points()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.ApproveIssueAsync(issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        _gamificationService.Verify(
            g => g.AwardPointsAsync(issueUser.Id, 50, It.IsAny<string>(), false),
            Times.Once);
    }

    [Fact]
    public async Task ApproveIssue_Should_Create_AdminAction()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.ApproveIssueAsync(issue.Id, new ApproveIssueRequest { AdminNotes = "Looks good" }, admin.SupabaseUserId);

        using var verifyCtx = _dbFactory.CreateContext();
        var action = await verifyCtx.AdminActions.FirstOrDefaultAsync(a => a.IssueId == issue.Id);
        action.Should().NotBeNull();
        action!.ActionType.Should().Be(AdminActionType.Approve);
        action.Notes.Should().Be("Looks good");
    }

    [Fact]
    public async Task ApproveIssue_Should_Reject_Non_Reviewable_Status()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Active);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.ApproveIssueAsync(issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not in a reviewable state");
    }

    [Fact]
    public async Task ApproveIssue_Should_Accept_UnderReview_Status()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.UnderReview);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.ApproveIssueAsync(issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        result.Success.Should().BeTrue();
    }

    // ── RejectIssueAsync ──

    [Fact]
    public async Task RejectIssue_Should_Set_Status_To_Rejected()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.RejectIssueAsync(issue.Id,
            new RejectIssueRequest { Reason = "Duplicate" },
            admin.SupabaseUserId);

        result.Success.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.Issues.FindAsync(issue.Id);
        updated!.Status.Should().Be(IssueStatus.Rejected);
        updated.RejectionReason.Should().Be("Duplicate");
    }

    [Fact]
    public async Task RejectIssue_Should_Reject_Non_Reviewable()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Active);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.RejectIssueAsync(issue.Id,
            new RejectIssueRequest { Reason = "Bad" },
            admin.SupabaseUserId);

        result.Success.Should().BeFalse();
    }

    // ── RequestChangesAsync ──

    [Fact]
    public async Task RequestChanges_Should_Set_UnderReview_And_Store_Notes()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.RequestChangesAsync(issue.Id,
            new RequestChangesRequest { RequestedChanges = "Add more photos" },
            admin.SupabaseUserId);

        result.Success.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.Issues.FindAsync(issue.Id);
        updated!.Status.Should().Be(IssueStatus.UnderReview);
        updated.AdminNotes.Should().Be("Add more photos");
    }

    // ── BulkApproveIssuesAsync ──

    [Fact]
    public async Task BulkApprove_Should_Process_Multiple_Issues()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var issue1 = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);
        var issue2 = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.AddRange(issue1, issue2);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.BulkApproveIssuesAsync(
            new BulkApproveRequest { IssueIds = [issue1.Id, issue2.Id] },
            admin.SupabaseUserId);

        result.SuccessfullyApproved.Should().Be(2);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task BulkApprove_Should_Reject_Over_50()
    {
        var issueIds = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();

        var svc = CreateService();
        var result = await svc.BulkApproveIssuesAsync(
            new BulkApproveRequest { IssueIds = issueIds },
            "admin-id");

        result.SuccessfullyApproved.Should().Be(0);
        result.Message.Should().Contain("Cannot approve more than 50");
    }

    [Fact]
    public async Task BulkApprove_Should_Handle_Partial_Failures()
    {
        var admin = TestDataBuilder.CreateUser();
        var issueUser = TestDataBuilder.CreateUser();
        var goodIssue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Submitted);
        var badIssue = TestDataBuilder.CreateIssue(userId: issueUser.Id, status: IssueStatus.Active); // Not reviewable

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(admin, issueUser);
            ctx.Issues.AddRange(goodIssue, badIssue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.BulkApproveIssuesAsync(
            new BulkApproveRequest { IssueIds = [goodIssue.Id, badIssue.Id] },
            admin.SupabaseUserId);

        result.SuccessfullyApproved.Should().Be(1);
        result.Failed.Should().Be(1);
    }

    // ── GetStatisticsAsync ──

    [Fact]
    public async Task GetStatistics_Should_Return_Correct_Counts()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Rejected),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Resolved),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Submitted)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var stats = await svc.GetStatisticsAsync("all");

        stats.TotalSubmissions.Should().Be(5);
        stats.Active.Should().Be(2);
        stats.Rejected.Should().Be(1);
        stats.Resolved.Should().Be(1);
        stats.PendingReview.Should().Be(1); // Submitted count
    }

    [Fact]
    public async Task GetStatistics_Should_Calculate_Approval_Rate()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            // 3 active + 1 rejected = 4 decisions, approval rate = 75%
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Rejected)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var stats = await svc.GetStatisticsAsync("all");

        stats.ApprovalRate.Should().Be(75.0);
    }
}
