using Civiti.Application.Requests.Admin;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Moderation;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Domain.Exceptions;
using Civiti.Infrastructure.Data;
using Civiti.Infrastructure.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

/// <summary>
/// Owner edit of an issue (<c>PUT /api/user/issues/{id}</c>) and the re-approval loop it drives.
/// Kept apart from <see cref="IssueServiceTests"/> because the edit path has its own concerns —
/// authorization, the editable-status machine, optimistic concurrency and the moderation gate.
/// </summary>
public class IssueServiceUpdateTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<IssueService>> _logger = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<IActivityService> _activityService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IAdminNotifier> _adminNotifier = new();
    private readonly Mock<IContentModerationService> _contentModerationService = new();
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

    public IssueServiceUpdateTests()
    {
        _contentModerationService
            .Setup(m => m.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ContentModerationResponse { IsAllowed = true });
    }

    private IssueService CreateService() => new(
        _logger.Object, _dbFactory.CreateContext(),
        _gamificationService.Object, _memoryCache,
        _activityService.Object, _notificationService.Object,
        _adminNotifier.Object, _contentModerationService.Object);

    public void Dispose()
    {
        _memoryCache.Dispose();
        _dbFactory.Dispose();
    }

    /// <summary>Seeds an owner plus one of their issues and returns both, as stored.</summary>
    private async Task<(UserProfile Owner, Issue Issue)> SeedIssueAsync(
        IssueStatus status,
        Action<Issue>? customize = null)
    {
        var owner = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id, status: status);
        customize?.Invoke(issue);

        using var ctx = _dbFactory.CreateContext();
        ctx.UserProfiles.Add(owner);
        ctx.Issues.Add(issue);
        await ctx.SaveChangesAsync();

        // Re-read so the caller holds the timestamps exactly as the database rounded them —
        // the same values a client would have received.
        Issue stored = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        return (owner, stored);
    }

    private static UpdateIssueRequest ValidRequest(DateTime expectedUpdatedAt) => new()
    {
        Title = "Updated title",
        Description = "Updated description with enough content",
        Category = IssueCategory.Environment,
        Address = "Strada Noua, Nr. 5",
        District = "Sector 3",
        Latitude = 44.5,
        Longitude = 26.2,
        Urgency = UrgencyLevel.High,
        DesiredOutcome = "Fix it",
        CommunityImpact = "Affects the whole street",
        Resubmit = true,
        ExpectedUpdatedAt = expectedUpdatedAt
    };

    // ── Happy path ──

    [Fact]
    public async Task UpdateIssue_Should_Replace_Every_Editable_Field_And_Resubmit()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        var result = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.Success);
        result.Issue!.Title.Should().Be("Updated title");
        result.Issue.Category.Should().Be(IssueCategory.Environment);
        result.Issue.Address.Should().Be("Strada Noua, Nr. 5");
        result.Issue.District.Should().Be("Sector 3");
        result.Issue.Latitude.Should().Be(44.5);
        result.Issue.Longitude.Should().Be(26.2);
        result.Issue.Urgency.Should().Be(UrgencyLevel.High);
        result.Issue.DesiredOutcome.Should().Be("Fix it");
        result.Issue.CommunityImpact.Should().Be("Affects the whole street");
        result.Issue.Status.Should().Be(IssueStatus.Submitted);
        result.Issue.UpdatedAt.Should().BeAfter(issue.UpdatedAt);

        using var ctx = _dbFactory.CreateContext();
        Issue persisted = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        persisted.Title.Should().Be("Updated title");
        persisted.Status.Should().Be(IssueStatus.Submitted);
    }

    [Fact]
    public async Task UpdateIssue_Should_Return_The_Original_Creator()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        var result = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        result.Issue!.User.Id.Should().Be(owner.Id);
        result.Issue.User.Name.Should().Be(owner.DisplayName);
    }

    // ── Authorization ──

    [Fact]
    public async Task UpdateIssue_Should_Reject_A_Caller_Who_Is_Not_The_Owner()
    {
        var (_, issue) = await SeedIssueAsync(IssueStatus.Rejected);
        var stranger = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(stranger);
            await ctx.SaveChangesAsync();
        }

        var result = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), stranger.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.NotOwner);

        using var verifyCtx = _dbFactory.CreateContext();
        Issue untouched = await verifyCtx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        untouched.Title.Should().Be(issue.Title);
        untouched.Status.Should().Be(IssueStatus.Rejected);
    }

    [Fact]
    public async Task UpdateIssue_Should_Report_A_Missing_Issue()
    {
        var (owner, _) = await SeedIssueAsync(IssueStatus.Rejected);

        var result = await CreateService().UpdateIssueAsync(
            Guid.NewGuid(), ValidRequest(DateTime.UtcNow), owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.IssueNotFound);
    }

    [Fact]
    public async Task UpdateIssue_Should_Reject_A_Deleted_Account()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);
        using (var ctx = _dbFactory.CreateContext())
        {
            UserProfile profile = await ctx.UserProfiles.FirstAsync(u => u.Id == owner.Id);
            profile.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var result = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.AccountDeleted);
    }

    // ── Editable-status machine ──

    [Theory]
    [InlineData(IssueStatus.Rejected, IssueStatus.Submitted)]
    [InlineData(IssueStatus.Submitted, IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview, IssueStatus.UnderReview)]
    public async Task UpdateIssue_Should_Land_On_The_Expected_Status(IssueStatus from, IssueStatus expected)
    {
        var (owner, issue) = await SeedIssueAsync(from);

        var result = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.Success);
        result.Issue!.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(IssueStatus.Resolved)]
    [InlineData(IssueStatus.Cancelled)]
    [InlineData(IssueStatus.Unspecified)]
    [InlineData(IssueStatus.Draft)]
    // Active is intentionally here for now: editing a live issue stays closed until the admin
    // re-review diff ships, because it would preserve supporter counters while silently
    // replacing the approved content.
    [InlineData(IssueStatus.Active)]
    public async Task UpdateIssue_Should_Refuse_A_Non_Editable_Status(IssueStatus status)
    {
        var (owner, issue) = await SeedIssueAsync(status);

        var result = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.StatusNotEditable);

        using var ctx = _dbFactory.CreateContext();
        Issue untouched = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        untouched.Title.Should().Be(issue.Title);
        untouched.Status.Should().Be(status);
    }

    [Fact]
    public async Task UpdateIssue_Should_Put_The_Issue_Back_In_The_Admin_Pending_Queue()
    {
        // End-to-end rather than an assertion about the status constant: the queue's own filter
        // is what decides whether a resubmitted issue ever reaches a human.
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        var adminService = new AdminService(
            Mock.Of<ILogger<AdminService>>(), _dbFactory.CreateContext(),
            _gamificationService.Object, _activityService.Object, _notificationService.Object);

        var pending = await adminService.GetPendingIssuesAsync(
            new GetPendingIssuesRequest { Page = 1, PageSize = 20 });

        pending.Items.Should().ContainSingle(i => i.Id == issue.Id);
    }

    // ── Optimistic concurrency ──

    [Fact]
    public async Task UpdateIssue_Should_Reject_A_Stale_Concurrency_Token()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        var result = await CreateService().UpdateIssueAsync(
            issue.Id,
            ValidRequest(issue.UpdatedAt.AddSeconds(-30)),
            owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.ConcurrencyConflict);

        using var ctx = _dbFactory.CreateContext();
        Issue untouched = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        untouched.Title.Should().Be(issue.Title);
        untouched.Status.Should().Be(IssueStatus.Rejected);
    }

    [Fact]
    public async Task UpdateIssue_Should_Accept_The_Token_It_Just_Handed_Back()
    {
        // The regression guard for the precision trap: an in-memory DateTime carries finer ticks
        // than the database stores, so a response timestamp that is not truncated can never equal
        // the stored one — and every second consecutive edit would fail with a bogus conflict.
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        var first = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);
        first.Outcome.Should().Be(UpdateIssueOutcome.Success);

        var second = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(first.Issue!.UpdatedAt), owner.SupabaseUserId);

        second.Outcome.Should().Be(UpdateIssueOutcome.Success);
    }

    [Fact]
    public async Task UpdateIssue_Should_Accept_A_Token_Sent_Without_A_Timezone()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        UpdateIssueRequest request = ValidRequest(
            DateTime.SpecifyKind(issue.UpdatedAt, DateTimeKind.Unspecified));

        var result = await CreateService().UpdateIssueAsync(issue.Id, request, owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.Success);
    }

    // ── Moderation and photo-URL safety ──

    [Fact]
    public async Task UpdateIssue_Should_Reject_Content_The_Moderator_Blocks()
    {
        // Without this gate the create-time moderation is worthless: publish something benign,
        // then edit the abusive text in and reach exactly the same read surfaces.
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        _contentModerationService
            .Setup(m => m.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ContentModerationResponse
            {
                IsAllowed = false,
                BlockReason = "test moderation block"
            });

        var act = () => CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        await act.Should().ThrowAsync<ContentModerationException>()
            .WithMessage("test moderation block");

        using var ctx = _dbFactory.CreateContext();
        Issue untouched = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        untouched.Title.Should().Be(issue.Title);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///etc/passwd")]
    public async Task UpdateIssue_Should_Reject_A_Non_Http_PhotoUrl(string url)
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        UpdateIssueRequest request = ValidRequest(issue.UpdatedAt);
        request.PhotoUrls = [url];

        var result = await CreateService().UpdateIssueAsync(issue.Id, request, owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.ValidationFailed);
        result.Error.Should().Contain("http or https");

        using var ctx = _dbFactory.CreateContext();
        Issue untouched = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        untouched.Title.Should().Be(issue.Title);
    }

    // ── Data preservation ──

    [Fact]
    public async Task UpdateIssue_Should_Preserve_The_Supporter_Counters()
    {
        var (owner, issue) = await SeedIssueAsync(
            IssueStatus.Rejected,
            i =>
            {
                i.EmailsSent = 12;
                i.CommunityVotes = 34;
            });

        var result = await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        result.Issue!.EmailsSent.Should().Be(12);
        result.Issue.CommunityVotes.Should().Be(34);
    }

    [Fact]
    public async Task UpdateIssue_Should_Clear_The_Previous_Reviews_Verdict()
    {
        var (owner, issue) = await SeedIssueAsync(
            IssueStatus.Rejected,
            i =>
            {
                i.RejectionReason = "Not enough detail";
                i.ReviewedAt = DateTime.UtcNow.AddDays(-1);
                i.ReviewedBy = "Admin One";
                i.AdminNotes = "Ask for a photo";
            });

        await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        using var ctx = _dbFactory.CreateContext();
        Issue persisted = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        persisted.RejectionReason.Should().BeNull();
        persisted.ReviewedAt.Should().BeNull();
        persisted.ReviewedBy.Should().BeNull();
        persisted.AdminNotes.Should().BeNull();
    }

    // ── Photos and authorities are replaced wholesale ──

    [Fact]
    public async Task UpdateIssue_Should_Replace_Photos_And_Make_The_First_One_Primary()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.IssuePhotos.Add(new IssuePhoto
            {
                Id = Guid.NewGuid(),
                IssueId = issue.Id,
                Url = "https://example.com/old.jpg",
                IsPrimary = true
            });
            await ctx.SaveChangesAsync();
        }

        UpdateIssueRequest request = ValidRequest(issue.UpdatedAt);
        request.PhotoUrls = ["https://example.com/new-first.jpg", "https://example.com/new-second.jpg"];

        var result = await CreateService().UpdateIssueAsync(issue.Id, request, owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.Success);
        result.Issue!.Photos.Should().HaveCount(2);
        result.Issue.Photos[0].Url.Should().Be("https://example.com/new-first.jpg");
        result.Issue.Photos[0].IsPrimary.Should().BeTrue();
        result.Issue.Photos[1].IsPrimary.Should().BeFalse();
        result.Issue.Photos.Should().NotContain(p => p.Url == "https://example.com/old.jpg");

        using var verifyCtx = _dbFactory.CreateContext();
        var storedUrls = await verifyCtx.IssuePhotos
            .Where(p => p.IssueId == issue.Id)
            .Select(p => p.Url)
            .ToListAsync();
        storedUrls.Should().BeEquivalentTo(
            ["https://example.com/new-first.jpg", "https://example.com/new-second.jpg"]);
    }

    [Fact]
    public async Task UpdateIssue_Should_Replace_Authorities_With_The_Submitted_Set()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.IssueAuthorities.Add(new IssueAuthority
            {
                Id = Guid.NewGuid(),
                IssueId = issue.Id,
                CustomName = "Old Authority",
                CustomEmail = "old@example.ro"
            });
            await ctx.SaveChangesAsync();
        }

        UpdateIssueRequest request = ValidRequest(issue.UpdatedAt);
        request.Authorities =
        [
            new IssueAuthorityInput { CustomName = "Primăria Sector 3", CustomEmail = "contact@ps3.ro" }
        ];

        var result = await CreateService().UpdateIssueAsync(issue.Id, request, owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.Success);
        result.Issue!.Authorities.Should().ContainSingle()
            .Which.Email.Should().Be("contact@ps3.ro");
    }

    [Fact]
    public async Task UpdateIssue_Should_Reject_An_Authority_That_Is_Both_Predefined_And_Custom()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        UpdateIssueRequest request = ValidRequest(issue.UpdatedAt);
        request.Authorities =
        [
            new IssueAuthorityInput
            {
                AuthorityId = Guid.NewGuid(),
                CustomName = "Both",
                CustomEmail = "both@example.ro"
            }
        ];

        var result = await CreateService().UpdateIssueAsync(issue.Id, request, owner.SupabaseUserId);

        result.Outcome.Should().Be(UpdateIssueOutcome.ValidationFailed);

        using var ctx = _dbFactory.CreateContext();
        Issue untouched = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        untouched.Title.Should().Be(issue.Title);
        untouched.Status.Should().Be(IssueStatus.Rejected);
    }

    // ── Audit trail and admin announcement ──

    [Fact]
    public async Task UpdateIssue_Should_Record_The_Resubmit_In_The_Moderation_History()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        using var ctx = _dbFactory.CreateContext();
        AdminAction action = await ctx.AdminActions.AsNoTracking()
            .SingleAsync(a => a.IssueId == issue.Id);

        action.ActionType.Should().Be(AdminActionType.Resubmit);
        action.AdminUserId.Should().Be(owner.Id);
        action.PreviousStatus.Should().Be(nameof(IssueStatus.Rejected));
        action.NewStatus.Should().Be(nameof(IssueStatus.Submitted));
    }

    [Fact]
    public async Task UpdateIssue_Should_Announce_The_Resubmit_To_Admins()
    {
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);

        await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        _adminNotifier.Verify(
            n => n.NotifyNewIssueAsync(issue.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateIssue_Should_Clear_The_Per_Admin_Notification_Markers()
    {
        // Those markers make a retrying dispatcher idempotent. A resubmit is a new announcement,
        // not a retry — leaving them behind would swallow the email and the issue would reappear
        // in the queue with nobody told.
        var (owner, issue) = await SeedIssueAsync(IssueStatus.Rejected);
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.AdminIssueNotifications.Add(new AdminIssueNotification
            {
                IssueId = issue.Id,
                AdminEmail = "admin@civiti.ro",
                EnqueuedAt = DateTime.UtcNow.AddDays(-1)
            });
            await ctx.SaveChangesAsync();
        }

        await CreateService().UpdateIssueAsync(
            issue.Id, ValidRequest(issue.UpdatedAt), owner.SupabaseUserId);

        using var verifyCtx = _dbFactory.CreateContext();
        (await verifyCtx.AdminIssueNotifications.AnyAsync(n => n.IssueId == issue.Id))
            .Should().BeFalse();
    }

    // ── Owner read of a non-public issue (the edit form's prefill) ──

    [Theory]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    public async Task GetIssueById_Should_Return_A_Non_Public_Issue_To_Its_Owner(IssueStatus status)
    {
        var (owner, issue) = await SeedIssueAsync(status);

        var response = await CreateService().GetIssueByIdAsync(issue.Id, owner.Id);

        response.Should().NotBeNull();
        response!.Id.Should().Be(issue.Id);
    }

    [Theory]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    public async Task GetIssueById_Should_Hide_A_Non_Public_Issue_From_Everyone_Else(IssueStatus status)
    {
        var (_, issue) = await SeedIssueAsync(status);
        var stranger = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(stranger);
            await ctx.SaveChangesAsync();
        }

        (await CreateService().GetIssueByIdAsync(issue.Id, stranger.Id)).Should().BeNull();
        (await CreateService().GetIssueByIdAsync(issue.Id)).Should().BeNull();
    }
}
