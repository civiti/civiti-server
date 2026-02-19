using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Auth;
using Civiti.Api.Services;
using Civiti.Api.Services.Interfaces;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<UserService>> _logger = new();
    private readonly Mock<IGamificationService> _gamificationService = new();
    private readonly Mock<INotificationService> _notificationService = new();

    private UserService CreateService()
    {
        var context = _dbFactory.CreateContext();
        return new UserService(_logger.Object, context, _gamificationService.Object, _notificationService.Object);
    }

    public UserServiceTests()
    {
        // Default: gamification queries return empty lists
        _gamificationService
            .Setup(g => g.GetUserBadgesAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new List<Api.Models.Responses.Gamification.BadgeResponse>());
        _gamificationService
            .Setup(g => g.GetUserAchievementsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new List<Api.Models.Responses.Gamification.AchievementProgressResponse>());
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

    // ── DeleteUserAsync ──

    [Fact]
    public async Task DeleteUser_Should_Anonymize_Data()
    {
        var user = TestDataBuilder.CreateUser(supabaseUserId: "delete_me", displayName: "Real Name", email: "real@test.com");
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.DeleteUserAsync("delete_me");

        result.Should().BeTrue();

        using var verifyCtx = _dbFactory.CreateContext();
        var deleted = await verifyCtx.UserProfiles.FindAsync(user.Id);
        deleted!.DisplayName.Should().Be("Deleted User");
        deleted.Email.Should().Contain("deleted_");
        deleted.PhotoUrl.Should().BeNull();
        deleted.County.Should().Be("Unknown");
        deleted.City.Should().Be("Unknown");
    }

    [Fact]
    public async Task DeleteUser_Should_Return_False_When_Not_Found()
    {
        var svc = CreateService();
        var result = await svc.DeleteUserAsync("nonexistent_user");
        result.Should().BeFalse();
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
}
