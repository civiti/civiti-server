using Civiti.Application.Diffing;
using Civiti.Application.Requests.Admin;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Admin;
using Civiti.Application.Responses.Moderation;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Infrastructure.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

/// <summary>
/// The approved-content snapshot and the diff it powers, end to end: approve → edit → re-review.
/// </summary>
public class AdminServiceSnapshotTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<IActivityService> _activityService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IAdminNotifier> _adminNotifier = new();
    private readonly Mock<IContentModerationService> _contentModerationService = new();
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

    public AdminServiceSnapshotTests()
    {
        _contentModerationService
            .Setup(m => m.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ContentModerationResponse { IsAllowed = true });
    }

    private AdminService CreateAdminService() => new(
        Mock.Of<ILogger<AdminService>>(), _dbFactory.CreateContext(),
        _gamificationService.Object, _activityService.Object, _notificationService.Object);

    private IssueService CreateIssueService() => new(
        Mock.Of<ILogger<IssueService>>(), _dbFactory.CreateContext(),
        _gamificationService.Object, _memoryCache,
        _activityService.Object, _notificationService.Object,
        _adminNotifier.Object, _contentModerationService.Object);

    public void Dispose()
    {
        _memoryCache.Dispose();
        _dbFactory.Dispose();
    }

    private async Task<(UserProfile Admin, UserProfile Owner, Issue Issue)> SeedAsync(
        IssueStatus status = IssueStatus.Submitted)
    {
        var admin = TestDataBuilder.CreateUser(displayName: "Admin One");
        var owner = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: owner.Id, status: status);

        using var ctx = _dbFactory.CreateContext();
        ctx.UserProfiles.AddRange(admin, owner);
        ctx.Issues.Add(issue);
        ctx.IssuePhotos.Add(new IssuePhoto
        {
            Id = Guid.NewGuid(),
            IssueId = issue.Id,
            Url = "https://example.com/approved.jpg",
            IsPrimary = true
        });
        ctx.IssueAuthorities.Add(new IssueAuthority
        {
            Id = Guid.NewGuid(),
            IssueId = issue.Id,
            CustomName = "Primăria Sector 1",
            CustomEmail = "contact@ps1.ro"
        });
        await ctx.SaveChangesAsync();

        Issue stored = await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == issue.Id);
        return (admin, owner, stored);
    }

    private static UpdateIssueRequest EditFrom(Issue issue, Action<UpdateIssueRequest>? customize = null)
    {
        UpdateIssueRequest request = new()
        {
            Title = issue.Title,
            Description = issue.Description,
            Category = issue.Category,
            Address = issue.Address,
            District = issue.District ?? string.Empty,
            Latitude = issue.Latitude,
            Longitude = issue.Longitude,
            Urgency = issue.Urgency,
            DesiredOutcome = issue.DesiredOutcome,
            CommunityImpact = issue.CommunityImpact,
            PhotoUrls = ["https://example.com/approved.jpg"],
            Authorities =
            [
                new IssueAuthorityInput { CustomName = "Primăria Sector 1", CustomEmail = "contact@ps1.ro" }
            ],
            Resubmit = true,
            ExpectedUpdatedAt = issue.UpdatedAt
        };

        customize?.Invoke(request);
        return request;
    }

    // ── Capture on approval ──

    [Fact]
    public async Task ApproveIssue_Should_Record_What_Was_Approved()
    {
        var (admin, _, issue) = await SeedAsync();

        await CreateAdminService().ApproveIssueAsync(
            issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        using var ctx = _dbFactory.CreateContext();
        IssueApprovedSnapshot snapshot = await ctx.IssueApprovedSnapshots.AsNoTracking()
            .SingleAsync(s => s.IssueId == issue.Id);

        snapshot.ApprovedByUserId.Should().Be(admin.Id);
        snapshot.Payload.Should().Contain(issue.Title);
        snapshot.Payload.Should().Contain("https://example.com/approved.jpg");
        snapshot.Payload.Should().Contain("contact@ps1.ro");
    }

    [Fact]
    public async Task BulkApprove_Should_Record_Snapshots_Too()
    {
        // Bulk approval routes through ApproveIssueAsync, and this pins that it stays that way.
        var (admin, owner, first) = await SeedAsync();
        var second = TestDataBuilder.CreateIssue(userId: owner.Id, status: IssueStatus.Submitted);
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.Issues.Add(second);
            await ctx.SaveChangesAsync();
        }

        await CreateAdminService().BulkApproveIssuesAsync(
            new BulkApproveRequest { IssueIds = [first.Id, second.Id] }, admin.SupabaseUserId);

        using var verifyCtx = _dbFactory.CreateContext();
        var snapshotted = await verifyCtx.IssueApprovedSnapshots.AsNoTracking()
            .Select(s => s.IssueId).ToListAsync();

        snapshotted.Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [Fact]
    public async Task Re_Approving_After_An_Edit_Should_Move_The_Baseline_Forward()
    {
        var (admin, owner, issue) = await SeedAsync();

        await CreateAdminService().ApproveIssueAsync(
            issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        Issue approved = await ReadIssueAsync(issue.Id);
        await CreateIssueService().UpdateIssueAsync(
            issue.Id, EditFrom(approved, r => r.Title = "Edited title"), owner.SupabaseUserId);

        await CreateAdminService().ApproveIssueAsync(
            issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        // A third look must now diff against the *edited* version, not the original.
        AdminIssueDetailResponse? detail =
            await CreateAdminService().GetIssueDetailsForAdminAsync(issue.Id);

        detail!.ApprovedSnapshot!.Title.Should().Be("Edited title");
        detail.ChangedFields.Should().BeEmpty();
    }

    // ── The diff on the re-review screen ──

    [Fact]
    public async Task Admin_Detail_Should_Report_No_Baseline_For_A_Never_Approved_Issue()
    {
        // Null snapshot means "first review" and must not be confused with "nothing changed".
        var (_, _, issue) = await SeedAsync();

        AdminIssueDetailResponse? detail =
            await CreateAdminService().GetIssueDetailsForAdminAsync(issue.Id);

        detail!.ApprovedSnapshot.Should().BeNull();
        detail.ChangedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_Detail_Should_Report_What_The_Owner_Changed_After_Approval()
    {
        var (admin, owner, issue) = await SeedAsync();

        await CreateAdminService().ApproveIssueAsync(
            issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        Issue approved = await ReadIssueAsync(issue.Id);
        var edit = EditFrom(approved, r =>
        {
            r.Title = "A completely different headline";
            r.Latitude = 45.5;
            r.Authorities =
            [
                new IssueAuthorityInput { CustomName = "Altă instituție", CustomEmail = "altceva@example.ro" }
            ];
        });

        var result = await CreateIssueService().UpdateIssueAsync(issue.Id, edit, owner.SupabaseUserId);
        result.Outcome.Should().Be(UpdateIssueOutcome.Success);

        AdminIssueDetailResponse? detail =
            await CreateAdminService().GetIssueDetailsForAdminAsync(issue.Id);

        detail!.ApprovedSnapshot.Should().NotBeNull();
        detail.ApprovedSnapshot!.Title.Should().Be(issue.Title, "the baseline is the approved version");
        detail.Title.Should().Be("A completely different headline", "the detail shows the pending version");

        detail.ChangedFields.Should().BeEquivalentTo(
            [IssueDiffFields.Title, IssueDiffFields.Location, IssueDiffFields.Authorities]);
    }

    [Fact]
    public async Task Admin_Detail_Should_Report_Nothing_Changed_For_An_Untouched_Resubmit()
    {
        var (admin, owner, issue) = await SeedAsync();

        await CreateAdminService().ApproveIssueAsync(
            issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        Issue approved = await ReadIssueAsync(issue.Id);
        await CreateIssueService().UpdateIssueAsync(
            issue.Id, EditFrom(approved), owner.SupabaseUserId);

        AdminIssueDetailResponse? detail =
            await CreateAdminService().GetIssueDetailsForAdminAsync(issue.Id);

        detail!.ApprovedSnapshot.Should().NotBeNull();
        detail.ChangedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task An_Edited_Live_Issue_Should_Survive_The_Full_Re_Approval_Loop()
    {
        // The acceptance path: approve → edit (pulled from public) → re-approve → public again,
        // with the supporter counters intact throughout.
        var (admin, owner, seeded) = await SeedAsync();
        using (var ctx = _dbFactory.CreateContext())
        {
            Issue withSupport = await ctx.Issues.FirstAsync(i => i.Id == seeded.Id);
            withSupport.EmailsSent = 12;
            withSupport.CommunityVotes = 34;
            await ctx.SaveChangesAsync();
        }

        await CreateAdminService().ApproveIssueAsync(
            seeded.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        Issue approved = await ReadIssueAsync(seeded.Id);
        approved.Status.Should().Be(IssueStatus.Active);

        await CreateIssueService().UpdateIssueAsync(
            seeded.Id, EditFrom(approved, r => r.Title = "Corrected title"), owner.SupabaseUserId);

        (await ReadIssueAsync(seeded.Id)).Status.Should().Be(IssueStatus.Submitted);

        var reApproval = await CreateAdminService().ApproveIssueAsync(
            seeded.Id, new ApproveIssueRequest(), admin.SupabaseUserId);
        reApproval.Success.Should().BeTrue();

        Issue final = await ReadIssueAsync(seeded.Id);
        final.Status.Should().Be(IssueStatus.Active);
        final.Title.Should().Be("Corrected title");
        final.EmailsSent.Should().Be(12);
        final.CommunityVotes.Should().Be(34);
    }

    // ── False positives: the diff is only useful if an untouched field stays quiet ──

    [Fact]
    public async Task A_Predefined_Authority_Should_Not_Register_As_Changed_When_Untouched()
    {
        // A predefined link carries no name or email of its own — they live on the Authority row.
        // If a capture path forgets to load it, the baseline stores blanks and every later diff
        // reports the authorities as changed: noise on exactly the field a reviewer most needs to
        // trust, since redirecting an approved petition elsewhere is the edit worth catching.
        var (admin, owner, issue) = await SeedAsync();

        var authority = new Authority
        {
            Id = Guid.NewGuid(),
            Name = "Primăria Municipiului București",
            Email = "contact@pmb.ro",
            County = "București",
            City = "București",
            IsActive = true
        };

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.Authorities.Add(authority);
            ctx.IssueAuthorities.RemoveRange(
                ctx.IssueAuthorities.Where(ia => ia.IssueId == issue.Id));
            ctx.IssueAuthorities.Add(new IssueAuthority
            {
                Id = Guid.NewGuid(),
                IssueId = issue.Id,
                AuthorityId = authority.Id
            });
            await ctx.SaveChangesAsync();
        }

        await CreateAdminService().ApproveIssueAsync(
            issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        Issue approved = await ReadIssueAsync(issue.Id);
        var edit = EditFrom(approved, r =>
        {
            r.Title = "Edited headline";
            r.Authorities = [new IssueAuthorityInput { AuthorityId = authority.Id }];
        });

        (await CreateIssueService().UpdateIssueAsync(issue.Id, edit, owner.SupabaseUserId))
            .Outcome.Should().Be(UpdateIssueOutcome.Success);

        AdminIssueDetailResponse? detail =
            await CreateAdminService().GetIssueDetailsForAdminAsync(issue.Id);

        detail!.ApprovedSnapshot!.Authorities.Should().ContainSingle()
            .Which.Email.Should().Be("contact@pmb.ro");
        detail.ChangedFields.Should().Equal([IssueDiffFields.Title]);
    }

    [Fact]
    public async Task A_Predefined_Authority_Should_Survive_The_Lazy_Capture_Path_Too()
    {
        // Same trap on the other capture path — a live issue snapshotted at edit time.
        var (_, owner, issue) = await SeedAsync(IssueStatus.Active);

        var authority = new Authority
        {
            Id = Guid.NewGuid(),
            Name = "Primăria Sectorului 4",
            Email = "contact@ps4.ro",
            County = "București",
            City = "București",
            IsActive = true
        };

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.Authorities.Add(authority);
            ctx.IssueAuthorities.RemoveRange(
                ctx.IssueAuthorities.Where(ia => ia.IssueId == issue.Id));
            ctx.IssueAuthorities.Add(new IssueAuthority
            {
                Id = Guid.NewGuid(),
                IssueId = issue.Id,
                AuthorityId = authority.Id
            });
            await ctx.SaveChangesAsync();
        }

        Issue live = await ReadIssueAsync(issue.Id);
        var edit = EditFrom(live, r =>
        {
            r.Title = "Edited headline";
            r.Authorities = [new IssueAuthorityInput { AuthorityId = authority.Id }];
        });

        (await CreateIssueService().UpdateIssueAsync(issue.Id, edit, owner.SupabaseUserId))
            .Outcome.Should().Be(UpdateIssueOutcome.Success);

        AdminIssueDetailResponse? detail =
            await CreateAdminService().GetIssueDetailsForAdminAsync(issue.Id);

        detail!.ApprovedSnapshot!.Authorities.Should().ContainSingle()
            .Which.Email.Should().Be("contact@ps4.ro");
        detail.ChangedFields.Should().Equal([IssueDiffFields.Title]);
    }

    [Fact]
    public async Task Resubmitting_The_Same_Photo_List_Should_Not_Register_As_Changed()
    {
        // Every photo in one edit shares a CreatedAt and gets a fresh GUID, so any ordering that
        // leans on those is effectively random — an unchanged list would come back reordered and
        // read as a change. Position is stored instead.
        var (admin, owner, issue) = await SeedAsync();

        List<string> photos =
        [
            "https://example.com/one.jpg",
            "https://example.com/two.jpg",
            "https://example.com/three.jpg"
        ];

        Issue beforeFirstEdit = await ReadIssueAsync(issue.Id);
        var seed = EditFrom(beforeFirstEdit, r => r.PhotoUrls = photos);
        (await CreateIssueService().UpdateIssueAsync(issue.Id, seed, owner.SupabaseUserId))
            .Outcome.Should().Be(UpdateIssueOutcome.Success);

        await CreateAdminService().ApproveIssueAsync(
            issue.Id, new ApproveIssueRequest(), admin.SupabaseUserId);

        Issue approved = await ReadIssueAsync(issue.Id);
        var resubmit = EditFrom(approved, r => r.PhotoUrls = photos);
        (await CreateIssueService().UpdateIssueAsync(issue.Id, resubmit, owner.SupabaseUserId))
            .Outcome.Should().Be(UpdateIssueOutcome.Success);

        AdminIssueDetailResponse? detail =
            await CreateAdminService().GetIssueDetailsForAdminAsync(issue.Id);

        detail!.ApprovedSnapshot!.PhotoUrls.Should().Equal(photos);
        detail.ChangedFields.Should().NotContain(IssueDiffFields.Photos);
        detail.Photos.Select(p => p.Url).Should().Equal(photos);
    }

    private async Task<Issue> ReadIssueAsync(Guid id)
    {
        using var ctx = _dbFactory.CreateContext();
        return await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == id);
    }
}
