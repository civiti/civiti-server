using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Exceptions;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Reports;
using Civiti.Api.Services;
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

        // Create 2 existing reports (threshold is 3)
        var reporter1 = TestDataBuilder.CreateUser();
        var reporter2 = TestDataBuilder.CreateUser();
        issue.ReportCount = 2;

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(issueAuthor, reporter1, reporter2);
            ctx.Issues.Add(issue);
            ctx.Reports.AddRange(
                TestDataBuilder.CreateReport(reporterId: reporter1.Id, targetType: ReportTargetTypes.Issue, targetId: issue.Id),
                TestDataBuilder.CreateReport(reporterId: reporter2.Id, targetType: ReportTargetTypes.Issue, targetId: issue.Id));
            await ctx.SaveChangesAsync();
        }

        // Third reporter triggers auto-flag
        var reporter3 = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(reporter3);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, _) = await svc.ReportIssueAsync(issue.Id, ValidRequest(), reporter3.SupabaseUserId);

        success.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var flaggedIssue = await verifyCtx.Issues.FindAsync(issue.Id);
        flaggedIssue!.IsFlagged.Should().BeTrue();
        flaggedIssue.ReportCount.Should().Be(3);
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
        comment.ReportCount = 2;

        var reporter1 = TestDataBuilder.CreateUser();
        var reporter2 = TestDataBuilder.CreateUser();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(commentAuthor, reporter1, reporter2);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            ctx.Reports.AddRange(
                TestDataBuilder.CreateReport(reporterId: reporter1.Id, targetType: ReportTargetTypes.Comment, targetId: comment.Id),
                TestDataBuilder.CreateReport(reporterId: reporter2.Id, targetType: ReportTargetTypes.Comment, targetId: comment.Id));
            await ctx.SaveChangesAsync();
        }

        var reporter3 = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(reporter3);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, _) = await svc.ReportCommentAsync(comment.Id, ValidRequest(), reporter3.SupabaseUserId);

        success.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var hiddenComment = await verifyCtx.Comments.FindAsync(comment.Id);
        hiddenComment!.IsHidden.Should().BeTrue();
        hiddenComment.ReportCount.Should().Be(3);
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
