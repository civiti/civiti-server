using Civiti.Api.Models.Domain;
using Civiti.Api.Services;
using Civiti.Api.Services.Interfaces;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class GamificationServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<GamificationService>> _logger = new();
    private readonly Mock<INotificationService> _notificationService = new();

    private GamificationService CreateService()
    {
        var context = _dbFactory.CreateContext();
        return new GamificationService(_logger.Object, context, _notificationService.Object);
    }

    public void Dispose() => _dbFactory.Dispose();

    // ── AwardPointsAsync ──

    [Fact]
    public async Task AwardPoints_Should_Add_Points_To_User()
    {
        var user = TestDataBuilder.CreateUser(points: 10, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.AwardPointsAsync(user.Id, 20, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Points.Should().Be(30);
    }

    [Fact]
    public async Task AwardPoints_Should_Silently_Return_When_User_Not_Found()
    {
        var svc = CreateService();

        // Should not throw
        await svc.AwardPointsAsync(Guid.NewGuid(), 10, "test");
    }

    [Fact]
    public async Task AwardPoints_Should_Trigger_Level_Up()
    {
        // Level 2 requires 50 points (level formula: (level-1)*50 + previous)
        var user = TestDataBuilder.CreateUser(points: 40, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.AwardPointsAsync(user.Id, 15, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Points.Should().Be(55);
        updated.Level.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task AwardPoints_Should_Queue_LevelUp_Notification()
    {
        var user = TestDataBuilder.CreateUser(points: 40, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.AwardPointsAsync(user.Id, 15, "test");

        _notificationService.Verify(
            n => n.NotifyLevelUpAsync(It.IsAny<UserProfile>(), It.IsAny<int>()),
            Times.Once);
    }

    // ── DeductPointsAsync ──

    [Fact]
    public async Task DeductPoints_Should_Subtract_Points()
    {
        var user = TestDataBuilder.CreateUser(points: 100, level: 2);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.DeductPointsAsync(user.Id, 30, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Points.Should().Be(70);
    }

    [Fact]
    public async Task DeductPoints_Should_Floor_At_Zero()
    {
        var user = TestDataBuilder.CreateUser(points: 10, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.DeductPointsAsync(user.Id, 50, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Points.Should().Be(0);
    }

    [Fact]
    public async Task DeductPoints_Should_Adjust_Level_Down()
    {
        // User at level 2 (50+ points). Deduct enough to drop below level 2 threshold
        var user = TestDataBuilder.CreateUser(points: 55, level: 2);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.DeductPointsAsync(user.Id, 50, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Points.Should().Be(5);
        updated.Level.Should().Be(1);
    }

    [Fact]
    public async Task DeductPoints_Should_Silently_Return_When_User_Not_Found()
    {
        var svc = CreateService();
        await svc.DeductPointsAsync(Guid.NewGuid(), 10, "test");
    }

    // ── CheckAndAwardBadgesAsync ──

    [Fact]
    public async Task CheckAndAwardBadges_Should_Award_When_Criteria_Met()
    {
        var user = TestDataBuilder.CreateUser(points: 0);
        user.IssuesReported = 5;
        var badge = TestDataBuilder.CreateBadge(
            requirementType: "issues_reported",
            requirementValue: 5,
            rarity: BadgeRarity.Common);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Badges.Add(badge);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.CheckAndAwardBadgesAsync(user.Id);

        using var verifyCtx = _dbFactory.CreateContext();
        var userBadges = verifyCtx.UserBadges.Where(ub => ub.UserId == user.Id).ToList();
        userBadges.Should().HaveCount(1);
        userBadges[0].BadgeId.Should().Be(badge.Id);
    }

    [Fact]
    public async Task CheckAndAwardBadges_Should_Skip_Already_Earned()
    {
        var user = TestDataBuilder.CreateUser();
        user.IssuesReported = 10;
        var badge = TestDataBuilder.CreateBadge(requirementType: "issues_reported", requirementValue: 5);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Badges.Add(badge);
            ctx.UserBadges.Add(new UserBadge
            {
                Id = Guid.NewGuid(), UserId = user.Id, BadgeId = badge.Id, EarnedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.CheckAndAwardBadgesAsync(user.Id);

        using var verifyCtx = _dbFactory.CreateContext();
        verifyCtx.UserBadges.Where(ub => ub.UserId == user.Id).Should().HaveCount(1);
    }

    [Fact]
    public async Task CheckAndAwardBadges_Should_Award_Rarity_Based_Points()
    {
        var user = TestDataBuilder.CreateUser(points: 0);
        user.IssuesReported = 5;
        var badge = TestDataBuilder.CreateBadge(
            requirementType: "issues_reported",
            requirementValue: 5,
            rarity: BadgeRarity.Rare);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Badges.Add(badge);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.CheckAndAwardBadgesAsync(user.Id);

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Points.Should().Be(200); // Rare = 200 points
    }

    [Fact]
    public async Task CheckAndAwardBadges_Should_Send_Notification()
    {
        var user = TestDataBuilder.CreateUser();
        user.IssuesReported = 5;
        var badge = TestDataBuilder.CreateBadge(
            name: "Reporter Badge",
            requirementType: "issues_reported",
            requirementValue: 5);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Badges.Add(badge);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.CheckAndAwardBadgesAsync(user.Id);

        _notificationService.Verify(
            n => n.NotifyBadgeEarnedAsync(It.IsAny<UserProfile>(), "Reporter Badge"),
            Times.Once);
    }

    // ── UpdateAchievementProgressAsync ──

    [Fact]
    public async Task UpdateAchievementProgress_Should_Create_And_Increment()
    {
        var user = TestDataBuilder.CreateUser();
        var achievement = TestDataBuilder.CreateAchievement(achievementType: "issues_reported", maxProgress: 5);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Achievements.Add(achievement);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.UpdateAchievementProgressAsync(user.Id, "issues_reported");

        using var verifyCtx = _dbFactory.CreateContext();
        var ua = verifyCtx.UserAchievements
            .FirstOrDefault(x => x.UserId == user.Id && x.AchievementId == achievement.Id);
        ua.Should().NotBeNull();
        ua!.Progress.Should().Be(1);
        ua.Completed.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAchievementProgress_Absolute_Should_Set_Value_Directly()
    {
        var user = TestDataBuilder.CreateUser();
        var achievement = TestDataBuilder.CreateAchievement(achievementType: "level_up", maxProgress: 10);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Achievements.Add(achievement);
            ctx.UserAchievements.Add(new UserAchievement
            {
                Id = Guid.NewGuid(), UserId = user.Id, AchievementId = achievement.Id, Progress = 3
            });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.UpdateAchievementProgressAsync(user.Id, "level_up", progress: 7, isAbsolute: true);

        using var verifyCtx = _dbFactory.CreateContext();
        var ua = verifyCtx.UserAchievements
            .First(x => x.UserId == user.Id && x.AchievementId == achievement.Id);
        ua.Progress.Should().Be(7);
    }

    [Fact]
    public async Task UpdateAchievementProgress_Should_Cap_At_MaxProgress()
    {
        var user = TestDataBuilder.CreateUser();
        var achievement = TestDataBuilder.CreateAchievement(achievementType: "test", maxProgress: 3, rewardPoints: 50);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Achievements.Add(achievement);
            ctx.UserAchievements.Add(new UserAchievement
            {
                Id = Guid.NewGuid(), UserId = user.Id, AchievementId = achievement.Id, Progress = 2
            });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.UpdateAchievementProgressAsync(user.Id, "test", progress: 5);

        using var verifyCtx = _dbFactory.CreateContext();
        var ua = verifyCtx.UserAchievements
            .First(x => x.UserId == user.Id && x.AchievementId == achievement.Id);
        ua.Progress.Should().Be(3);
        ua.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAchievementProgress_Should_Award_Points_On_Completion()
    {
        var user = TestDataBuilder.CreateUser(points: 0);
        var achievement = TestDataBuilder.CreateAchievement(
            achievementType: "test", maxProgress: 1, rewardPoints: 100);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            ctx.Achievements.Add(achievement);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.UpdateAchievementProgressAsync(user.Id, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Points.Should().Be(100);
    }

    // ── Level Calculation Edge Cases ──

    [Fact]
    public async Task Level_Should_Be_1_At_Zero_Points()
    {
        var user = TestDataBuilder.CreateUser(points: 0, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.AwardPointsAsync(user.Id, 0, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Level.Should().Be(1);
    }

    [Fact]
    public async Task Level_Should_Be_1_At_49_Points()
    {
        var user = TestDataBuilder.CreateUser(points: 0, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.AwardPointsAsync(user.Id, 49, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Level.Should().Be(1);
    }

    [Fact]
    public async Task Level_Should_Be_2_At_50_Points()
    {
        var user = TestDataBuilder.CreateUser(points: 0, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.AwardPointsAsync(user.Id, 50, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Level.Should().Be(2);
    }

    [Fact]
    public async Task Level_Should_Be_3_At_150_Points()
    {
        // Level 3 requires: (2)*50 + GetPointsForLevel(2) = 100 + 50 = 150
        var user = TestDataBuilder.CreateUser(points: 0, level: 1);
        using (var ctx = _dbFactory.CreateContext()) { ctx.UserProfiles.Add(user); await ctx.SaveChangesAsync(); }

        var svc = CreateService();
        await svc.AwardPointsAsync(user.Id, 150, "test");

        using var verifyCtx = _dbFactory.CreateContext();
        var updated = await verifyCtx.UserProfiles.FindAsync(user.Id);
        updated!.Level.Should().Be(3);
    }
}
