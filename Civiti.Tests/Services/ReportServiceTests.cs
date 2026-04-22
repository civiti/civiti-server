using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Domain.Entities;
using Civiti.Application.Requests.Reports;
using Civiti.Infrastructure.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class ReportServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<ReportService>> _logger = new();

    private ReportService CreateService()
    {
        var context = _dbFactory.CreateContext();
        return new ReportService(_logger.Object, context);
    }

    public void Dispose() => _dbFactory.Dispose();

    private static CreateReportRequest ValidRequest() => new()
    {
        Reason = "Spam",
        Details = "This is spam content"
    };

    // ── ReportIssueAsync ──

    [Fact]
    public async Task ReportIssue_Should_Succeed()
    {
        var reporter = TestDataBuilder.CreateUser();
        var issueAuthor = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueAuthor.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, issueAuthor);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, reportId, error) = await svc.ReportIssueAsync(issue.Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeTrue();
        reportId.Should().NotBeNull();
        error.Should().BeNull();

        // Verify report was persisted
        using var verifyCtx = _dbFactory.CreateContext();
        var report = await verifyCtx.Reports.FindAsync(reportId!.Value);
        report.Should().NotBeNull();
        report!.TargetType.Should().Be(ReportTargetTypes.Issue);
        report.TargetId.Should().Be(issue.Id);
        report.Reason.Should().Be(ReportReason.Spam);
    }

    [Fact]
    public async Task ReportIssue_Should_Return_Conflict_On_Duplicate()
    {
        var reporter = TestDataBuilder.CreateUser();
        var issueAuthor = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueAuthor.Id);
        var existingReport = TestDataBuilder.CreateReport(
            reporterId: reporter.Id,
            targetType: ReportTargetTypes.Issue,
            targetId: issue.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, issueAuthor);
            ctx.Issues.Add(issue);
            ctx.Reports.Add(existingReport);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportIssueAsync(issue.Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.AlreadyReported);
    }

    [Fact]
    public async Task ReportIssue_Should_Fail_For_Own_Content()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportIssueAsync(issue.Id, ValidRequest(), user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.CannotReportOwnContent);
    }

    [Fact]
    public async Task ReportIssue_Should_AutoFlag_After_Threshold()
    {
        var issueAuthor = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: issueAuthor.Id);

        // Create 14 existing reports (threshold is 15)
        var existingReporters = Enumerable.Range(0, 14).Select(_ => TestDataBuilder.CreateUser()).ToList();
        issue.ReportCount = 14;

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(new[] { issueAuthor }.Concat(existingReporters));
            ctx.Issues.Add(issue);
            ctx.Reports.AddRange(existingReporters.Select(r =>
                TestDataBuilder.CreateReport(reporterId: r.Id, targetType: ReportTargetTypes.Issue, targetId: issue.Id)));
            await ctx.SaveChangesAsync();
        }

        // 15th reporter triggers auto-flag
        var triggeringReporter = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(triggeringReporter);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, _) = await svc.ReportIssueAsync(issue.Id, ValidRequest(), triggeringReporter.SupabaseUserId);

        success.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var flaggedIssue = await verifyCtx.Issues.FindAsync(issue.Id);
        flaggedIssue!.IsFlagged.Should().BeTrue();
        flaggedIssue.ReportCount.Should().Be(15);
    }

    [Fact]
    public async Task ReportIssue_Should_Fail_For_Nonexistent_Issue()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportIssueAsync(Guid.NewGuid(), ValidRequest(), user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.IssueNotFound);
    }

    [Fact]
    public async Task ReportIssue_Should_Fail_For_NonActive_Issue()
    {
        var reporter = TestDataBuilder.CreateUser();
        var author = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: author.Id);
        issue.Status = IssueStatus.Draft;

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, author);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportIssueAsync(issue.Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.IssueNotReportable);
    }

    [Fact]
    public async Task ReportIssue_Should_Throw_For_Deleted_Account()
    {
        var user = TestDataBuilder.CreateUser();
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.Invoking(s => s.ReportIssueAsync(Guid.NewGuid(), ValidRequest(), user.SupabaseUserId))
            .Should().ThrowAsync<AccountDeletedException>();
    }

    [Fact]
    public async Task ReportIssue_Should_Return_RateLimited_After_5_Reports()
    {
        var reporter = TestDataBuilder.CreateUser();
        var authors = Enumerable.Range(0, 6).Select(_ => TestDataBuilder.CreateUser()).ToList();
        var issues = authors.Select(a => TestDataBuilder.CreateIssue(userId: a.Id)).ToList();

        var existingReports = issues.Take(5).Select(i =>
            TestDataBuilder.CreateReport(reporterId: reporter.Id, targetType: ReportTargetTypes.Issue, targetId: i.Id)).ToList();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(new[] { reporter }.Concat(authors));
            ctx.Issues.AddRange(issues);
            ctx.Reports.AddRange(existingReports);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportIssueAsync(issues[5].Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.ReportRateLimited);
    }

    [Fact]
    public async Task ReportIssue_Should_Not_Be_RateLimited_For_Old_Reports()
    {
        var reporter = TestDataBuilder.CreateUser();
        var authors = Enumerable.Range(0, 6).Select(_ => TestDataBuilder.CreateUser()).ToList();
        var issues = authors.Select(a => TestDataBuilder.CreateIssue(userId: a.Id)).ToList();

        // 5 reports older than 1 hour should NOT count toward the rate limit
        var existingReports = issues.Take(5).Select(i =>
            TestDataBuilder.CreateReport(
                reporterId: reporter.Id,
                targetType: ReportTargetTypes.Issue,
                targetId: i.Id,
                createdAt: DateTime.UtcNow.AddHours(-2))).ToList();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(new[] { reporter }.Concat(authors));
            ctx.Issues.AddRange(issues);
            ctx.Reports.AddRange(existingReports);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, reportId, error) = await svc.ReportIssueAsync(issues[5].Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeTrue();
        reportId.Should().NotBeNull();
        error.Should().BeNull();
    }

    // ── ReportCommentAsync ──

    [Fact]
    public async Task ReportComment_Should_Succeed()
    {
        var reporter = TestDataBuilder.CreateUser();
        var commentAuthor = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: commentAuthor.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: commentAuthor.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, commentAuthor);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, reportId, error) = await svc.ReportCommentAsync(comment.Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeTrue();
        reportId.Should().NotBeNull();
        error.Should().BeNull();
    }

    [Fact]
    public async Task ReportComment_Should_AutoHide_After_Threshold()
    {
        var commentAuthor = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: commentAuthor.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: commentAuthor.Id);
        comment.ReportCount = 14;

        // Create 14 existing reports (threshold is 15)
        var existingReporters = Enumerable.Range(0, 14).Select(_ => TestDataBuilder.CreateUser()).ToList();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(new[] { commentAuthor }.Concat(existingReporters));
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            ctx.Reports.AddRange(existingReporters.Select(r =>
                TestDataBuilder.CreateReport(reporterId: r.Id, targetType: ReportTargetTypes.Comment, targetId: comment.Id)));
            await ctx.SaveChangesAsync();
        }

        // 15th reporter triggers auto-hide
        var triggeringReporter = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(triggeringReporter);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, _) = await svc.ReportCommentAsync(comment.Id, ValidRequest(), triggeringReporter.SupabaseUserId);

        success.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var hiddenComment = await verifyCtx.Comments.FindAsync(comment.Id);
        hiddenComment!.IsHidden.Should().BeTrue();
        hiddenComment.ReportCount.Should().Be(15);
    }

    [Fact]
    public async Task ReportComment_Should_Fail_For_NonActive_Issue()
    {
        var reporter = TestDataBuilder.CreateUser();
        var author = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: author.Id);
        issue.Status = IssueStatus.Rejected;
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: author.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, author);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportCommentAsync(comment.Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.CommentNotReportable);
    }

    [Fact]
    public async Task ReportComment_Should_Fail_For_Deleted_Comment()
    {
        var reporter = TestDataBuilder.CreateUser();
        var commentAuthor = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: commentAuthor.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: commentAuthor.Id, isDeleted: true);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, commentAuthor);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportCommentAsync(comment.Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.CommentNotFound);
    }

    [Fact]
    public async Task ReportComment_Should_Return_Conflict_On_Duplicate()
    {
        var reporter = TestDataBuilder.CreateUser();
        var commentAuthor = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: commentAuthor.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: commentAuthor.Id);
        var existingReport = TestDataBuilder.CreateReport(
            reporterId: reporter.Id, targetType: ReportTargetTypes.Comment, targetId: comment.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, commentAuthor);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            ctx.Reports.Add(existingReport);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportCommentAsync(comment.Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.AlreadyReported);
    }

    [Fact]
    public async Task ReportComment_Should_Fail_For_Own_Content()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportCommentAsync(comment.Id, ValidRequest(), user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.CannotReportOwnContent);
    }

    [Fact]
    public async Task ReportComment_Should_Return_RateLimited_After_5_Reports()
    {
        var reporter = TestDataBuilder.CreateUser();
        var author = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: author.Id);
        var comments = Enumerable.Range(0, 6)
            .Select(_ => TestDataBuilder.CreateComment(issueId: issue.Id, userId: author.Id))
            .ToList();

        var existingReports = comments.Take(5)
            .Select(c => TestDataBuilder.CreateReport(
                reporterId: reporter.Id, targetType: ReportTargetTypes.Comment, targetId: c.Id))
            .ToList();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, author);
            ctx.Issues.Add(issue);
            ctx.Comments.AddRange(comments);
            ctx.Reports.AddRange(existingReports);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.ReportCommentAsync(
            comments[5].Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.ReportRateLimited);
    }

    [Fact]
    public async Task ReportComment_Should_Not_Be_RateLimited_For_Old_Reports()
    {
        var reporter = TestDataBuilder.CreateUser();
        var author = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: author.Id);
        var comments = Enumerable.Range(0, 6)
            .Select(_ => TestDataBuilder.CreateComment(issueId: issue.Id, userId: author.Id))
            .ToList();

        // 5 reports older than 1 hour should NOT count toward the rate limit
        var existingReports = comments.Take(5)
            .Select(c => TestDataBuilder.CreateReport(
                reporterId: reporter.Id,
                targetType: ReportTargetTypes.Comment,
                targetId: c.Id,
                createdAt: DateTime.UtcNow.AddHours(-2)))
            .ToList();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(reporter, author);
            ctx.Issues.Add(issue);
            ctx.Comments.AddRange(comments);
            ctx.Reports.AddRange(existingReports);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, reportId, error) = await svc.ReportCommentAsync(
            comments[5].Id, ValidRequest(), reporter.SupabaseUserId);

        success.Should().BeTrue();
        reportId.Should().NotBeNull();
        error.Should().BeNull();
    }

    [Fact]
    public async Task ReportComment_Should_Throw_For_Deleted_Account()
    {
        var user = TestDataBuilder.CreateUser();
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.Invoking(s => s.ReportCommentAsync(Guid.NewGuid(), ValidRequest(), user.SupabaseUserId))
            .Should().ThrowAsync<AccountDeletedException>();
    }
}
