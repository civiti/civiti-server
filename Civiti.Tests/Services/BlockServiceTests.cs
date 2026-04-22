using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Api.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class BlockServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<BlockService>> _logger = new();

    private BlockService CreateService()
    {
        var context = _dbFactory.CreateContext();
        return new BlockService(_logger.Object, context);
    }

    public void Dispose() => _dbFactory.Dispose();

    // ── BlockUserAsync ──

    [Fact]
    public async Task BlockUser_Should_Succeed()
    {
        var user = TestDataBuilder.CreateUser();
        var target = TestDataBuilder.CreateUser();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(user, target);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, data, error) = await svc.BlockUserAsync(target.Id, user.SupabaseUserId);

        success.Should().BeTrue();
        data.Should().NotBeNull();
        data!.BlockedUserId.Should().Be(target.Id);
        error.Should().BeNull();

        // Verify persisted
        using var verifyCtx = _dbFactory.CreateContext();
        var blocked = await verifyCtx.BlockedUsers
            .FirstOrDefaultAsync(b => b.UserId == user.Id && b.BlockedUserId == target.Id);
        blocked.Should().NotBeNull();
    }

    [Fact]
    public async Task BlockUser_Should_Fail_For_Self_Block()
    {
        var user = TestDataBuilder.CreateUser();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.BlockUserAsync(user.Id, user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.CannotBlockSelf);
    }

    [Fact]
    public async Task BlockUser_Should_Return_Conflict_On_Duplicate()
    {
        var user = TestDataBuilder.CreateUser();
        var target = TestDataBuilder.CreateUser();
        var existingBlock = TestDataBuilder.CreateBlockedUser(userId: user.Id, blockedUserId: target.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(user, target);
            ctx.BlockedUsers.Add(existingBlock);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.BlockUserAsync(target.Id, user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.AlreadyBlocked);
    }

    [Fact]
    public async Task BlockUser_Should_Fail_For_Nonexistent_Target()
    {
        var user = TestDataBuilder.CreateUser();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, _, error) = await svc.BlockUserAsync(Guid.NewGuid(), user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.TargetUserNotFound);
    }

    [Fact]
    public async Task BlockUser_Should_Throw_For_Deleted_Account()
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
        await svc.Invoking(s => s.BlockUserAsync(Guid.NewGuid(), user.SupabaseUserId))
            .Should().ThrowAsync<AccountDeletedException>();
    }

    // ── UnblockUserAsync ──

    [Fact]
    public async Task UnblockUser_Should_Succeed()
    {
        var user = TestDataBuilder.CreateUser();
        var target = TestDataBuilder.CreateUser();
        var block = TestDataBuilder.CreateBlockedUser(userId: user.Id, blockedUserId: target.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(user, target);
            ctx.BlockedUsers.Add(block);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.UnblockUserAsync(target.Id, user.SupabaseUserId);

        success.Should().BeTrue();
        error.Should().BeNull();

        // Verify removed
        using var verifyCtx = _dbFactory.CreateContext();
        var remaining = await verifyCtx.BlockedUsers
            .AnyAsync(b => b.UserId == user.Id && b.BlockedUserId == target.Id);
        remaining.Should().BeFalse();
    }

    [Fact]
    public async Task UnblockUser_Should_Fail_When_Not_Blocked()
    {
        var user = TestDataBuilder.CreateUser();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, error) = await svc.UnblockUserAsync(Guid.NewGuid(), user.SupabaseUserId);

        success.Should().BeFalse();
        error.Should().Be(DomainErrors.UserNotBlocked);
    }

    // ── GetBlockedUsersAsync ──

    [Fact]
    public async Task GetBlockedUsers_Should_Return_List_With_Profiles()
    {
        var user = TestDataBuilder.CreateUser();
        var target1 = TestDataBuilder.CreateUser(displayName: "Blocked User 1");
        var target2 = TestDataBuilder.CreateUser(displayName: "Blocked User 2");
        var block1 = TestDataBuilder.CreateBlockedUser(userId: user.Id, blockedUserId: target1.Id);
        var block2 = TestDataBuilder.CreateBlockedUser(userId: user.Id, blockedUserId: target2.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(user, target1, target2);
            ctx.BlockedUsers.AddRange(block1, block2);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, data, error) = await svc.GetBlockedUsersAsync(user.SupabaseUserId);

        success.Should().BeTrue();
        data.Should().HaveCount(2);
        data.Should().NotBeNull();
        data!.Select(b => b.DisplayName).Should().Contain("Blocked User 1")
            .And.Contain("Blocked User 2");
    }

    [Fact]
    public async Task GetBlockedUsers_Should_Return_DeletedUser_Placeholder()
    {
        var user = TestDataBuilder.CreateUser();
        var deletedTarget = TestDataBuilder.CreateUser(displayName: "Gone User");
        deletedTarget.IsDeleted = true;
        deletedTarget.DeletedAt = DateTime.UtcNow;
        var block = TestDataBuilder.CreateBlockedUser(userId: user.Id, blockedUserId: deletedTarget.Id);

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.AddRange(user, deletedTarget);
            ctx.BlockedUsers.Add(block);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, data, _) = await svc.GetBlockedUsersAsync(user.SupabaseUserId);

        success.Should().BeTrue();
        data.Should().HaveCount(1);
        data![0].UserId.Should().Be(deletedTarget.Id);
        data[0].DisplayName.Should().Be("Deleted User");
        data[0].PhotoUrl.Should().BeNull();
    }

    [Fact]
    public async Task GetBlockedUsers_Should_Return_Empty_List()
    {
        var user = TestDataBuilder.CreateUser();

        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.UserProfiles.Add(user);
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        var (success, data, _) = await svc.GetBlockedUsersAsync(user.SupabaseUserId);

        success.Should().BeTrue();
        data.Should().BeEmpty();
    }
}
