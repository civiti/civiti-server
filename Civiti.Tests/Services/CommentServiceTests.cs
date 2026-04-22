using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Civiti.Api.Models.Requests.Comments;
using Civiti.Api.Models.Responses.Moderation;
using Civiti.Api.Services;
using Civiti.Api.Services.Interfaces;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class CommentServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<CommentService>> _logger = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<IActivityService> _activityService = new();
    private readonly Mock<IContentModerationService> _contentModerationService = new();
    private readonly Mock<INotificationService> _notificationService = new();

    private CommentService CreateService()
    {
        var context = _dbFactory.CreateContext();
        return new CommentService(
            _logger.Object, context,
            _gamificationService.Object, _activityService.Object,
            _contentModerationService.Object, _notificationService.Object);
    }

    public CommentServiceTests()
    {
        // Default: allow all content
        _contentModerationService
            .Setup(m => m.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ContentModerationResponse { IsAllowed = true });
    }

    public void Dispose() => _dbFactory.Dispose();

    // ── GetIssueCommentsAsync ──

    [Fact]
    public async Task GetIssueComments_Should_Return_Null_For_NonExistent_Issue()
    {
        var svc = CreateService();
        var result = await svc.GetIssueCommentsAsync(Guid.NewGuid(),
            new GetCommentsRequest { Page = 1, PageSize = 10 }, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetIssueComments_Should_Exclude_Deleted()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            ctx.Comments.AddRange(
                TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id),
                TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id, isDeleted: true),
                TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetIssueCommentsAsync(issue.Id,
            new GetCommentsRequest { Page = 1, PageSize = 10 }, null);

        result!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetIssueComments_Should_Sort_By_Helpful()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            ctx.Comments.AddRange(
                TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id, helpfulCount: 1),
                TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id, helpfulCount: 10),
                TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id, helpfulCount: 5)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetIssueCommentsAsync(issue.Id,
            new GetCommentsRequest { Page = 1, PageSize = 10, SortBy = "helpful", SortDescending = true }, null);

        result!.Items[0].HelpfulCount.Should().Be(10);
    }

    // ── CreateCommentAsync ──

    [Fact]
    public async Task CreateComment_Should_Reject_When_User_Not_Found()
    {
        var svc = CreateService();

        var act = () => svc.CreateCommentAsync(Guid.NewGuid(),
            new CreateCommentRequest { Content = "Test" }, "nonexistent");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(DomainErrors.UserNotFound);
    }

    [Fact]
    public async Task CreateComment_Should_Reject_When_Issue_Not_Found()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.CreateCommentAsync(Guid.NewGuid(),
            new CreateCommentRequest { Content = "Test" }, user.SupabaseUserId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(DomainErrors.IssueNotFound);
    }

    [Fact]
    public async Task CreateComment_Should_Reject_NonActive_Issue()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.CreateCommentAsync(issue.Id,
            new CreateCommentRequest { Content = "Test" }, user.SupabaseUserId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cannot comment on non-active issues");
    }

    [Fact]
    public async Task CreateComment_Should_Reject_Flagged_Content()
    {
        _contentModerationService
            .Setup(m => m.ModerateContentAsync("bad content"))
            .ReturnsAsync(new ContentModerationResponse
            {
                IsAllowed = false,
                BlockReason = "Inappropriate language"
            });

        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.CreateCommentAsync(issue.Id,
            new CreateCommentRequest { Content = "bad content" }, user.SupabaseUserId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Inappropriate language");
    }

    [Fact]
    public async Task CreateComment_Should_Validate_Parent_Exists()
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
        var act = () => svc.CreateCommentAsync(issue.Id,
            new CreateCommentRequest { Content = "Reply", ParentCommentId = Guid.NewGuid() },
            user.SupabaseUserId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(DomainErrors.ParentCommentNotFound);
    }

    [Fact]
    public async Task CreateComment_Should_Reject_Empty_Content()
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
        var act = () => svc.CreateCommentAsync(issue.Id,
            new CreateCommentRequest { Content = "   " }, user.SupabaseUserId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty or whitespace*");
    }

    // ── UpdateCommentAsync ──

    [Fact]
    public async Task UpdateComment_Should_Set_IsEdited()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: user.Id, content: "Original");

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.UpdateCommentAsync(comment.Id,
            new UpdateCommentRequest { Content = "Updated" }, user.SupabaseUserId);

        success.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.Comments.FindAsync(comment.Id);
        updated!.Content.Should().Be("Updated");
        updated.IsEdited.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateComment_Should_Reject_NonOwner()
    {
        var owner = TestDataBuilder.CreateUser();
        var other = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: owner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(owner, other);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.UpdateCommentAsync(comment.Id,
            new UpdateCommentRequest { Content = "Hacked!" }, other.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Contain("only edit your own");
    }

    [Fact]
    public async Task UpdateComment_Should_Re_Moderate_Content()
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

        _contentModerationService
            .Setup(m => m.ModerateContentAsync("bad update"))
            .ReturnsAsync(new ContentModerationResponse { IsAllowed = false, BlockReason = "Nope" });

        var svc = CreateService();
        var (success, error) = await svc.UpdateCommentAsync(comment.Id,
            new UpdateCommentRequest { Content = "bad update" }, user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be("Nope");
    }

    // ── DeleteCommentAsync ──

    [Fact]
    public async Task DeleteComment_Should_Reject_NonOwner_NonAdmin()
    {
        var owner = TestDataBuilder.CreateUser();
        var other = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: owner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(owner, other);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.DeleteCommentAsync(comment.Id, other.SupabaseUserId, isAdmin: false);

        success.Should().BeFalse();
        error.Should().Contain("only delete your own");
    }

    // ── VoteHelpfulAsync ──

    [Fact]
    public async Task VoteHelpful_Should_Reject_Self_Vote()
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
        var (success, error) = await svc.VoteHelpfulAsync(comment.Id, user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Contain("cannot vote on your own");
    }

    [Fact]
    public async Task VoteHelpful_Should_Reject_Duplicate()
    {
        var commentOwner = TestDataBuilder.CreateUser();
        var voter = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: commentOwner.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: commentOwner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(commentOwner, voter);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            ctx.CommentVotes.Add(TestDataBuilder.CreateCommentVote(commentId: comment.Id, userId: voter.Id));
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.VoteHelpfulAsync(comment.Id, voter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Contain("already voted");
    }

    [Fact]
    public async Task VoteHelpful_Should_Reject_NonExistent_Comment()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.VoteHelpfulAsync(Guid.NewGuid(), user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.CommentNotFound);
    }

    [Fact]
    public async Task VoteHelpful_Should_Reject_Deleted_Comment()
    {
        var commentOwner = TestDataBuilder.CreateUser();
        var voter = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: commentOwner.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: commentOwner.Id, isDeleted: true);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(commentOwner, voter);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.VoteHelpfulAsync(comment.Id, voter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.CommentNotFound);
    }

    // ── RemoveVoteAsync ──

    [Fact]
    public async Task RemoveVote_Should_Reject_When_Not_Voted()
    {
        var commentOwner = TestDataBuilder.CreateUser();
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: commentOwner.Id);
        var comment = TestDataBuilder.CreateComment(issueId: issue.Id, userId: commentOwner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(commentOwner, user);
            ctx.Issues.Add(issue);
            ctx.Comments.Add(comment);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.RemoveVoteAsync(comment.Id, user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Contain("not voted");
    }
}
