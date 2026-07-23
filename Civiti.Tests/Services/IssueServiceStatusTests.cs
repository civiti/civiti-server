using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Moderation;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

/// <summary>
/// Owner-driven status changes (<c>PUT /api/user/issues/{id}/status</c>).
/// <para>
/// This is the other door into public visibility, and it has to be held to the same standard as
/// the edit path: <see cref="IssueStatus.Resolved"/> is publicly viewable, so anything that can
/// reach it can publish content.
/// </para>
/// </summary>
public class IssueServiceStatusTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<IActivityService> _activityService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IAdminNotifier> _adminNotifier = new();
    private readonly Mock<IContentModerationService> _contentModerationService = new();
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

    public IssueServiceStatusTests()
    {
        _contentModerationService
            .Setup(m => m.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ContentModerationResponse { IsAllowed = true });
    }

    private Infrastructure.Services.IssueService CreateService() => new(
        Mock.Of<ILogger<Infrastructure.Services.IssueService>>(), _dbFactory.CreateContext(),
        _gamificationService.Object, _memoryCache,
        _activityService.Object, _notificationService.Object,
        _adminNotifier.Object, _contentModerationService.Object);

    public void Dispose()
    {
        _memoryCache.Dispose();
        _dbFactory.Dispose();
    }

    private async Task<(UserProfile Owner, Issue Issue)> SeedAsync(IssueStatus status)
    {
        var owner = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id, status: status);

        using var ctx = _dbFactory.CreateContext();
        ctx.UserProfiles.Add(owner);
        ctx.Issues.Add(issue);
        await ctx.SaveChangesAsync();

        return (owner, await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id));
    }

    [Theory]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.Draft)]
    public async Task Owner_Should_Not_Be_Able_To_Publish_Unreviewed_Content_By_Resolving_It(
        IssueStatus neverApprovedStatus)
    {
        // Resolved is publicly viewable. If an owner can move an issue there from a status that
        // no admin has approved, they can publish whatever they like without review — and every
        // other moderation guard becomes theatre.
        var (owner, issue) = await SeedAsync(neverApprovedStatus);

        var (success, error) = await CreateService().UpdateIssueStatusAsync(
            issue.Id,
            new UpdateIssueStatusRequest { Status = IssueStatus.Resolved },
            owner.SupabaseUserId);

        success.Should().BeFalse(
            "an issue that was never approved must not be publishable by its own author");
        error.Should().NotBeNull();

        using var ctx = _dbFactory.CreateContext();
        Issue stored = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        stored.Status.Should().Be(neverApprovedStatus);
    }

    [Fact]
    public async Task Owner_Should_Not_Be_Able_To_Republish_An_Edited_Issue_By_Resolving_It()
    {
        // The bypass that would defeat the re-review diff: edit a live issue so it drops out of
        // public view pending re-approval, then resolve it straight back into public view with
        // the unreviewed content and every supporter counter intact.
        var (owner, issue) = await SeedAsync(IssueStatus.Active);

        var edit = new UpdateIssueRequest
        {
            Title = "Swapped content",
            Description = "Replaced after approval, never re-reviewed",
            Category = IssueCategory.Other,
            Address = issue.Address,
            District = issue.District ?? "Sector 1",
            Latitude = issue.Latitude,
            Longitude = issue.Longitude,
            Resubmit = true,
            ExpectedUpdatedAt = issue.UpdatedAt
        };

        (await CreateService().UpdateIssueAsync(issue.Id, edit, owner.SupabaseUserId))
            .Outcome.Should().Be(UpdateIssueOutcome.Success);

        var (success, _) = await CreateService().UpdateIssueStatusAsync(
            issue.Id,
            new UpdateIssueStatusRequest { Status = IssueStatus.Resolved },
            owner.SupabaseUserId);

        success.Should().BeFalse("resolving must not be a way around re-review");

        var publicList = await CreateService().GetAllIssuesAsync(
            new GetIssuesRequest { Page = 1, PageSize = 50, Statuses = [IssueStatus.Active, IssueStatus.Resolved] });
        publicList.Items.Should().NotContain(i => i.Id == issue.Id,
            "the edited content must stay out of public view until an admin re-approves it");

        (await CreateService().GetIssueByIdAsync(issue.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Owner_Should_Still_Be_Able_To_Resolve_A_Live_Issue()
    {
        // The legitimate case the endpoint exists for: the authority fixed the problem.
        var (owner, issue) = await SeedAsync(IssueStatus.Active);

        var (success, error) = await CreateService().UpdateIssueStatusAsync(
            issue.Id,
            new UpdateIssueStatusRequest { Status = IssueStatus.Resolved },
            owner.SupabaseUserId);

        success.Should().BeTrue(error);

        using var ctx = _dbFactory.CreateContext();
        (await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id))
            .Status.Should().Be(IssueStatus.Resolved);
    }

    [Theory]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.Active)]
    public async Task Owner_Should_Still_Be_Able_To_Cancel_At_Any_Stage(IssueStatus from)
    {
        // Withdrawing your own submission is legitimate whatever stage it is at, and Cancelled
        // is not publicly viewable, so it carries none of the same risk.
        var (owner, issue) = await SeedAsync(from);

        var (success, error) = await CreateService().UpdateIssueStatusAsync(
            issue.Id,
            new UpdateIssueStatusRequest { Status = IssueStatus.Cancelled },
            owner.SupabaseUserId);

        success.Should().BeTrue(error);
    }

    [Fact]
    public async Task A_Stranger_Should_Not_Be_Able_To_Change_Someone_Elses_Status()
    {
        var (_, issue) = await SeedAsync(IssueStatus.Active);
        var stranger = TestDataBuilder.CreateUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(stranger);
            await ctx.SaveChangesAsync();
        }

        var (success, _) = await CreateService().UpdateIssueStatusAsync(
            issue.Id,
            new UpdateIssueStatusRequest { Status = IssueStatus.Resolved },
            stranger.SupabaseUserId);

        success.Should().BeFalse();
    }
}
