using Civiti.Infrastructure.Data;
using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Civiti.Domain.Exceptions;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Moderation;
using Civiti.Infrastructure.Services;
using Civiti.Application.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class IssueServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<IssueService>> _logger = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<IActivityService> _activityService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IAdminNotifier> _adminNotifier = new();
    private readonly Mock<IContentModerationService> _contentModerationService = new();
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

    public IssueServiceTests()
    {
        // Default: allow all content. Tests that need to assert moderation rejection
        // override this on a per-test basis, mirroring the CommentServiceTests precedent.
        _contentModerationService
            .Setup(m => m.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ContentModerationResponse { IsAllowed = true });
    }

    private IssueService CreateService(CivitiDbContext? context = null)
    {
        context ??= _dbFactory.CreateContext();
        return new IssueService(
            _logger.Object, context,
            _gamificationService.Object, _memoryCache,
            _activityService.Object, _notificationService.Object,
            _adminNotifier.Object, _contentModerationService.Object);
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
        _dbFactory.Dispose();
    }

    // ── GetAllIssuesAsync ──

    [Fact]
    public async Task GetAllIssues_Should_Return_Only_Active_By_Default()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Submitted),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Rejected)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest { Page = 1, PageSize = 10 });

        result.Items.Should().HaveCount(2);
        result.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task GetAllIssues_Should_Filter_By_Category()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, category: IssueCategory.Infrastructure),
                TestDataBuilder.CreateIssue(userId: user.Id, category: IssueCategory.Environment),
                TestDataBuilder.CreateIssue(userId: user.Id, category: IssueCategory.Infrastructure)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest
        {
            Page = 1, PageSize = 10,
            Category = IssueCategory.Infrastructure
        });

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllIssues_Should_Filter_By_Urgency()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, urgency: UrgencyLevel.High),
                TestDataBuilder.CreateIssue(userId: user.Id, urgency: UrgencyLevel.Low),
                TestDataBuilder.CreateIssue(userId: user.Id, urgency: UrgencyLevel.High)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest
        {
            Page = 1, PageSize = 10,
            Urgency = UrgencyLevel.High
        });

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllIssues_Should_Sort_By_Votes_Descending()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, communityVotes: 5),
                TestDataBuilder.CreateIssue(userId: user.Id, communityVotes: 50),
                TestDataBuilder.CreateIssue(userId: user.Id, communityVotes: 20)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest
        {
            Page = 1, PageSize = 10,
            SortBy = "votes", SortDescending = true
        });

        result.Items[0].CommunityVotes.Should().Be(50);
        result.Items[1].CommunityVotes.Should().Be(20);
        result.Items[2].CommunityVotes.Should().Be(5);
    }

    [Fact]
    public async Task GetAllIssues_Should_Paginate()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            for (int i = 0; i < 15; i++)
                ctx.Issues.Add(TestDataBuilder.CreateIssue(userId: user.Id));
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var page1 = await svc.GetAllIssuesAsync(new GetIssuesRequest { Page = 1, PageSize = 10 });
        var page2 = await svc.GetAllIssuesAsync(new GetIssuesRequest { Page = 2, PageSize = 10 });

        page1.Items.Should().HaveCount(10);
        page2.Items.Should().HaveCount(5);
        page1.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetAllIssues_HasVoted_Should_Be_Null_For_Unauthenticated()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(TestDataBuilder.CreateIssue(userId: user.Id));
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest { Page = 1, PageSize = 10 }, currentUserId: null);

        result.Items[0].HasVoted.Should().BeNull();
    }

    [Fact]
    public async Task GetAllIssues_HasVoted_Should_Be_Null_For_Owner()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(TestDataBuilder.CreateIssue(userId: user.Id));
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest { Page = 1, PageSize = 10 }, currentUserId: user.Id);

        result.Items[0].HasVoted.Should().BeNull();
    }

    [Fact]
    public async Task GetAllIssues_HasVoted_Should_Be_True_When_Voted()
    {
        var owner = TestDataBuilder.CreateUser();
        var voter = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(owner, voter);
            ctx.Issues.Add(issue);
            ctx.IssueVotes.Add(TestDataBuilder.CreateIssueVote(issueId: issue.Id, userId: voter.Id));
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest { Page = 1, PageSize = 10 }, currentUserId: voter.Id);

        result.Items[0].HasVoted.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllIssues_HasVoted_Should_Be_False_When_Not_Voted()
    {
        var owner = TestDataBuilder.CreateUser();
        var nonVoter = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(owner, nonVoter);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetAllIssuesAsync(new GetIssuesRequest { Page = 1, PageSize = 10 }, currentUserId: nonVoter.Id);

        result.Items[0].HasVoted.Should().BeFalse();
    }

    // ── GetIssueByIdAsync ──

    [Fact]
    public async Task GetIssueById_Should_Return_Null_For_NonExistent()
    {
        var svc = CreateService();
        var result = await svc.GetIssueByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetIssueById_Should_Return_Active_Issue()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id, title: "Test Title");

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetIssueByIdAsync(issue.Id);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Title");
    }

    [Fact]
    public async Task GetIssueById_Should_Not_Return_Submitted_To_NonOwner()
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
        var result = await svc.GetIssueByIdAsync(issue.Id, currentUserId: Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetIssueById_Should_Return_Submitted_To_Owner()
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
        var result = await svc.GetIssueByIdAsync(issue.Id, currentUserId: user.Id);

        result.Should().NotBeNull();
    }

    // ── IncrementEmailCountAsync ──

    [Fact]
    public async Task IncrementEmailCount_Should_Increment_For_Active_Issue()
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id, emailsSent: 10);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.IncrementEmailCountAsync(issue.Id, "127.0.0.1");

        success.Should().BeTrue();
        error.Should().BeNull();

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.Issues.FindAsync(issue.Id);
        updated!.EmailsSent.Should().Be(11);
    }

    [Fact]
    public async Task IncrementEmailCount_Should_Rate_Limit_Same_Ip()
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
        await svc.IncrementEmailCountAsync(issue.Id, "127.0.0.1");

        // Second call from same IP should be rate limited
        var svc2 = CreateService();
        var (success, error) = await svc2.IncrementEmailCountAsync(issue.Id, "127.0.0.1");

        success.Should().BeFalse();
        error.Should().Be(IIssueService.RateLimitedError);
    }

    [Fact]
    public async Task IncrementEmailCount_Should_Reject_NonActive_Issue()
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
        var (success, error) = await svc.IncrementEmailCountAsync(issue.Id, "127.0.0.1");

        success.Should().BeFalse();
        error.Should().Be("Issue is not active");
    }

    [Fact]
    public async Task IncrementEmailCount_Should_Return_Error_For_NonExistent()
    {
        var svc = CreateService();
        var (success, error) = await svc.IncrementEmailCountAsync(Guid.NewGuid(), "127.0.0.1");

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.IssueNotFound);
    }

    // ── VoteForIssueAsync ──
    // Note: VoteForIssueAsync uses CreateExecutionStrategy + BeginTransactionAsync which requires
    // special handling for SQLite. We test the pre-validation paths here.

    [Fact]
    public async Task VoteForIssue_Should_Reject_When_User_Not_Found()
    {
        var svc = CreateService();
        var (success, error) = await svc.VoteForIssueAsync(Guid.NewGuid(), "nonexistent");

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.UserNotFound);
    }

    [Fact]
    public async Task VoteForIssue_Should_Reject_When_Issue_Not_Found()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.VoteForIssueAsync(Guid.NewGuid(), user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.IssueNotFound);
    }

    [Fact]
    public async Task VoteForIssue_Should_Reject_NonActive_Issue()
    {
        var owner = TestDataBuilder.CreateUser();
        var voter = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id, status: IssueStatus.Submitted);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(owner, voter);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.VoteForIssueAsync(issue.Id, voter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be("Can only vote on active issues");
    }

    [Fact]
    public async Task VoteForIssue_Should_Reject_Self_Vote()
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
        var (success, error) = await svc.VoteForIssueAsync(issue.Id, user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Contain("cannot vote on your own");
    }

    [Fact]
    public async Task VoteForIssue_Should_Reject_Duplicate_Vote()
    {
        var owner = TestDataBuilder.CreateUser();
        var voter = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(owner, voter);
            ctx.Issues.Add(issue);
            ctx.IssueVotes.Add(TestDataBuilder.CreateIssueVote(issueId: issue.Id, userId: voter.Id));
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.VoteForIssueAsync(issue.Id, voter.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Contain("already voted");
    }

    // ── RemoveVoteAsync ──

    [Fact]
    public async Task RemoveVote_Should_Reject_When_Not_Voted()
    {
        var owner = TestDataBuilder.CreateUser();
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(owner, user);
            ctx.Issues.Add(issue);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.RemoveVoteAsync(issue.Id, user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Contain("not voted");
    }

    // ── GetUserIssuesAsync ──

    [Fact]
    public async Task GetUserIssues_Should_Return_All_Statuses_For_Owner()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Submitted),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Rejected)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetUserIssuesAsync(user.SupabaseUserId,
            new GetUserIssuesRequest { Page = 1, PageSize = 10 });

        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetUserIssues_Should_Filter_By_Status()
    {
        var user = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Issues.AddRange(
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Active),
                TestDataBuilder.CreateIssue(userId: user.Id, status: IssueStatus.Submitted)
            );
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetUserIssuesAsync(user.SupabaseUserId,
            new GetUserIssuesRequest { Page = 1, PageSize = 10, Status = IssueStatus.Active });

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUserIssues_Should_Throw_For_Unknown_User()
    {
        var svc = CreateService();

        var act = () => svc.GetUserIssuesAsync("nonexistent",
            new GetUserIssuesRequest { Page = 1, PageSize = 10 });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(DomainErrors.UserProfileNotFound);
    }

    // ── CreateIssueAsync ──

    [Fact]
    public async Task CreateIssue_Should_Throw_ContentModerationException_When_Moderation_Rejects()
    {
        // Override the default-allow setup with a deny so we exercise the rejection path
        // added in this PR. The service should throw ContentModerationException before any
        // DB transaction is opened (moderation runs pre-transaction to avoid holding a
        // pooled connection across the OpenAI RTT).
        _contentModerationService
            .Setup(m => m.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ContentModerationResponse
            {
                IsAllowed = false,
                BlockReason = "test moderation block"
            });

        var user = TestDataBuilder.CreateUser(supabaseUserId: "moderate_create_user");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.CreateIssueAsync(
            new CreateIssueRequest
            {
                Title = "Bad title",
                Description = "Bad description",
                Category = IssueCategory.Infrastructure,
                Address = "Str. Test 1",
                District = "Sector 1",
                Latitude = 44.4,
                Longitude = 26.1
            },
            "moderate_create_user");

        await act.Should().ThrowAsync<ContentModerationException>()
            .WithMessage("test moderation block");
    }

    [Fact]
    public async Task CreateIssue_Should_Throw_For_Non_Http_PhotoUrl()
    {
        // PhotoUrls validation must reject javascript:/data:/file: URIs before they reach
        // the DB — same defense added on the profile photo path in this PR.
        var user = TestDataBuilder.CreateUser(supabaseUserId: "bad_photo_user");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.CreateIssueAsync(
            new CreateIssueRequest
            {
                Title = "Title",
                Description = "Description",
                Category = IssueCategory.Infrastructure,
                Address = "Str. Test 1",
                District = "Sector 1",
                Latitude = 44.4,
                Longitude = 26.1,
                PhotoUrls = new List<string> { "javascript:alert(1)" }
            },
            "bad_photo_user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*http or https*");
    }

    [Fact]
    public async Task CreateIssue_Should_Persist_Issue_And_Invoke_Gamification()
    {
        // Happy path: a valid submission is created and gamification is invoked. Guards the
        // post-commit restructure against accidentally dropping the gamification call — the
        // two pre-existing create tests only cover pre-transaction throws.
        var user = TestDataBuilder.CreateUser(supabaseUserId: "create_ok_user");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.CreateIssueAsync(
            new CreateIssueRequest
            {
                Title = "Valid title",
                Description = "Valid description",
                Category = IssueCategory.Infrastructure,
                Address = "Str. Test 1",
                District = "Sector 1",
                Latitude = 44.4,
                Longitude = 26.1
            },
            "create_ok_user");

        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);

        using var verifyCtx = _dbFactory.CreateContext();
        var persisted = await verifyCtx.Issues.FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(IssueStatus.Submitted);

        _gamificationService.Verify(
            g => g.AwardPointsAsync(user.Id, 10, It.IsAny<string>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateIssue_Should_Still_Create_Issue_When_Gamification_Throws()
    {
        // Gamification is a best-effort post-commit side effect: a failure in it (the
        // mid-enumeration InvalidOperationException, a constraint violation, etc.) must never
        // roll back or fail the issue the user validly created. Pre-fix, gamification ran
        // inside the transaction and any throw rolled the issue back and surfaced as HTTP 400.
        var user = TestDataBuilder.CreateUser(supabaseUserId: "gami_throws_user");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        _gamificationService
            .Setup(g => g.AwardPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException(
                "Collection was modified; enumeration operation may not execute."));

        var svc = CreateService();
        var result = await svc.CreateIssueAsync(
            new CreateIssueRequest
            {
                Title = "Resilient title",
                Description = "Resilient description",
                Category = IssueCategory.Infrastructure,
                Address = "Str. Test 1",
                District = "Sector 1",
                Latitude = 44.4,
                Longitude = 26.1
            },
            "gami_throws_user");

        // Issue creation succeeds and is persisted despite the gamification failure.
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);

        using var verifyCtx = _dbFactory.CreateContext();
        var persisted = await verifyCtx.Issues.FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.Title.Should().Be("Resilient title");
    }
}
