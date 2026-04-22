using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Domain.Entities;
using Civiti.Application.Requests.Auth;
using Civiti.Infrastructure.Services;
using Civiti.Application.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<UserService>> _logger = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<ISupabaseService> _supabaseService = new();

    private UserService CreateService()
    {
        var context = _dbFactory.CreateContext();
        return new UserService(_logger.Object, context, _gamificationService.Object, _notificationService.Object, _supabaseService.Object);
    }

    public UserServiceTests()
    {
        // Default: gamification queries return empty lists
        _gamificationService
            .Setup(g => g.GetUserBadgesAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new List<Application.Responses.Gamification.BadgeResponse>());
        _gamificationService
            .Setup(g => g.GetUserAchievementsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new List<Application.Responses.Gamification.AchievementProgressResponse>());

        // Default: Supabase auth deletion succeeds
        _supabaseService
            .Setup(s => s.DeleteAuthUserAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
    }

    public void Dispose() => _dbFactory.Dispose();

    // ── CreateUserProfileAsync ──

    [Fact]
    public async Task CreateUserProfile_Should_Create_With_Defaults()
    {
        var svc = CreateService();
        var supabaseId = "supabase_new_user";

        var result = await svc.CreateUserProfileAsync(
            new CreateUserProfileRequest { DisplayName = "New User" },
            supabaseId,
            "new@test.com");

        result.Should().NotBeNull();
        result.DisplayName.Should().Be("New User");
        result.Email.Should().Be("new@test.com");
        result.Level.Should().Be(1);
        result.Points.Should().Be(0);
    }

    [Fact]
    public async Task CreateUserProfile_Should_Reject_Duplicate()
    {
        var existing = TestDataBuilder.CreateUser(supabaseUserId: "dup_user");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(existing);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.CreateUserProfileAsync(
            new CreateUserProfileRequest { DisplayName = "Dup" },
            "dup_user",
            "dup@test.com");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("User profile already exists");
    }

    [Fact]
    public async Task CreateUserProfile_Should_Send_Welcome_Email()
    {
        var svc = CreateService();

        await svc.CreateUserProfileAsync(
            new CreateUserProfileRequest { DisplayName = "Welcome User" },
            "supabase_welcome",
            "welcome@test.com");

        _notificationService.Verify(
            n => n.NotifyWelcomeAsync(It.IsAny<UserProfile>()),
            Times.Once);
    }

    // ── UpdateUserProfileAsync ──

    [Fact]
    public async Task UpdateUserProfile_Should_Apply_Partial_Update()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "update_user", displayName: "Old Name");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.UpdateUserProfileAsync("update_user",
            new UpdateUserProfileRequest { DisplayName = "New Name" });

        result.DisplayName.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateUserProfile_Should_Throw_When_Not_Found()
    {
        var svc = CreateService();

        var act = () => svc.UpdateUserProfileAsync("nonexistent",
            new UpdateUserProfileRequest { DisplayName = "X" });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateUserProfile_Should_Throw_For_Deleted_User()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "deleted_update_user");
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow.AddDays(-1);
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.UpdateUserProfileAsync(
            "deleted_update_user",
            new UpdateUserProfileRequest { DisplayName = "X" });

        await act.Should().ThrowAsync<AccountDeletedException>();
    }

    // ── DeleteUserAsync ──

    [Fact]
    public async Task DeleteUser_Should_Anonymize_All_PII_And_Set_Flags()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "delete_me", displayName: "Real Name", email: "real@test.com");
        user.Phone = "+40712345678";
        user.ResidenceType = ResidenceType.Apartment;
        user.IssueUpdatesEnabled = true;
        user.CommunityNewsEnabled = true;
        user.MonthlyDigestEnabled = true;
        user.AchievementsEnabled = true;
        user.PushNotificationsEnabled = true;

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.PushTokens.Add(new PushToken { UserId = user.Id, Token = "ExponentPushToken[test]", Platform = PushTokenPlatform.Ios });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.DeleteUserAsync("delete_me");

        result.Should().Be(DeleteUserResult.Deleted);

        using var verifyCtx = _dbFactory.CreateContext();
        var deleted = await verifyCtx.UserProfiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        // PII anonymized
        deleted!.DisplayName.Should().Be("Deleted User");
        deleted.Email.Should().StartWith("deleted_").And.EndWith("@civica.ro");
        deleted.Email.Should().NotContain(user.Id.ToString()); // opaque hash, not reversible ID
        deleted.PhotoUrl.Should().BeNull();
        deleted.Phone.Should().BeNull();
        deleted.County.Should().Be("Unknown");
        deleted.City.Should().Be("Unknown");
        deleted.District.Should().Be("Unknown");
        deleted.ResidenceType.Should().BeNull();
        deleted.SupabaseUserId.Should().Be("delete_me"); // preserved for deleted-user lookup guard

        // Deletion flags
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
        deleted.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Notification preferences disabled
        deleted.IssueUpdatesEnabled.Should().BeFalse();
        deleted.CommunityNewsEnabled.Should().BeFalse();
        deleted.MonthlyDigestEnabled.Should().BeFalse();
        deleted.AchievementsEnabled.Should().BeFalse();
        deleted.PushNotificationsEnabled.Should().BeFalse();

        // Push tokens removed
        (await verifyCtx.PushTokens.AnyAsync(pt => pt.UserId == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUser_Should_Call_Supabase_Auth_Deletion()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "supa_delete");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.DeleteUserAsync("supa_delete");

        _supabaseService.Verify(
            s => s.DeleteAuthUserAsync("supa_delete"),
            Times.Once);
    }

    [Fact]
    public async Task DeleteUser_Should_Succeed_Even_When_Supabase_Fails()
    {
        _supabaseService
            .Setup(s => s.DeleteAuthUserAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var user = TestDataBuilder.CreateUser(supabaseUserId: "supa_fail_delete");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.DeleteUserAsync("supa_fail_delete");

        result.Should().Be(DeleteUserResult.Deleted);

        using var verifyCtx = _dbFactory.CreateContext();
        var deleted = await verifyCtx.UserProfiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);
        deleted!.IsDeleted.Should().BeTrue();
        deleted.DisplayName.Should().Be("Deleted User");
    }

    [Fact]
    public async Task DeleteUser_Should_Return_NotFound_When_Not_Found()
    {
        var svc = CreateService();
        var result = await svc.DeleteUserAsync("nonexistent_user");
        result.Should().Be(DeleteUserResult.NotFound);
    }

    [Fact]
    public async Task DeleteUser_Should_Return_AlreadyDeleted_And_Retry_Supabase_When_Already_Deleted()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "already_deleted");
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow.AddDays(-1);
        // Already soft-deleted locally — retry path should detect via IgnoreQueryFilters
        // and retry Supabase auth cleanup, returning true since local deletion is complete
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.DeleteUserAsync("already_deleted");

        result.Should().Be(DeleteUserResult.AlreadyDeleted);
        _supabaseService.Verify(s => s.DeleteAuthUserAsync("already_deleted"), Times.Once);
    }

    // ── GetUserProfileAsync ──

    [Fact]
    public async Task GetUserProfile_Should_Return_Null_When_Not_Found()
    {
        var svc = CreateService();
        var result = await svc.GetUserProfileAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfile_Should_Return_Profile_When_Exists()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "existing_user", displayName: "Existing");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetUserProfileAsync("existing_user");

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Existing");
    }

    [Fact]
    public async Task GetUserProfile_Should_Throw_For_Deleted_User()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "deleted_profile_user", displayName: "Gone");
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.GetUserProfileAsync("deleted_profile_user");

        await act.Should().ThrowAsync<AccountDeletedException>();
    }

    // ── GetOrCreateUserProfileAsync ──

    [Fact]
    public async Task GetOrCreate_Should_Return_Existing_Profile()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "existing", displayName: "Already Here");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.GetOrCreateUserProfileAsync("existing", "x@test.com", "New Name", null);

        result.DisplayName.Should().Be("Already Here"); // Returns existing, not new
    }

    [Fact]
    public async Task GetOrCreate_Should_Create_When_Not_Exists()
    {
        var svc = CreateService();
        var result = await svc.GetOrCreateUserProfileAsync("brand_new", "new@test.com", "Brand New", null);

        result.Should().NotBeNull();
        result.Email.Should().Be("new@test.com");
    }

    // ── Soft-delete re-creation guards ──

    [Fact]
    public async Task GetOrCreate_Should_Block_ReCreation_For_Deleted_User()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "deleted_user");
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow.AddDays(-1);
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.GetOrCreateUserProfileAsync("deleted_user", "x@test.com", "New Name", null);

        await act.Should().ThrowAsync<AccountDeletedException>();
    }

    [Fact]
    public async Task CreateUserProfile_Should_Block_ReCreation_For_Deleted_User()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "deleted_user_create");
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow.AddDays(-1);
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var act = () => svc.CreateUserProfileAsync(
            new CreateUserProfileRequest { DisplayName = "Reborn" },
            "deleted_user_create",
            "reborn@test.com");

        await act.Should().ThrowAsync<AccountDeletedException>();
    }

    // Note: GetLeaderboardAsync cannot be tested with SQLite due to SQL APPLY limitation
    // (GroupBy + Take in subquery). The !IsDeleted guard is verified by code review and
    // integration tests against PostgreSQL.
}
