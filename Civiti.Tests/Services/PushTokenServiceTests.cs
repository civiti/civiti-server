using Civiti.Api.Data;
using Civiti.Api.Models.Domain;
using Civiti.Api.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class PushTokenServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<PushTokenService>> _logger = new();
    private CivitiDbContext? _serviceCtx;

    public void Dispose()
    {
        _serviceCtx?.Dispose();
        _dbFactory.Dispose();
    }

    private PushTokenService CreateService()
    {
        _serviceCtx = _dbFactory.CreateContext();
        return new(_serviceCtx, _logger.Object);
    }

    private Guid SeedUser()
    {
        var user = TestDataBuilder.CreateUser();
        using var ctx = _dbFactory.CreateContext();
        ctx.UserProfiles.Add(user);
        ctx.SaveChanges();
        return user.Id;
    }

    // ── RegisterTokenAsync: insert new token ──

    [Fact]
    public async Task Register_Should_Insert_New_Token()
    {
        var userId = SeedUser();
        var svc = CreateService();

        await svc.RegisterTokenAsync(userId, "ExponentPushToken[new]", "ios");

        using var ctx = _dbFactory.CreateContext();
        var token = await ctx.PushTokens.SingleAsync(pt => pt.UserId == userId);
        token.Token.Should().Be("ExponentPushToken[new]");
        token.Platform.Should().Be(PushTokenPlatform.Ios);
    }

    // ── RegisterTokenAsync: update existing token ──

    [Fact]
    public async Task Register_Should_Update_Existing_Token()
    {
        var userId = SeedUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.PushTokens.Add(new PushToken { UserId = userId, Token = "ExponentPushToken[existing]", Platform = PushTokenPlatform.Ios });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[existing]", "android");

        using var verifyCtx = _dbFactory.CreateContext();
        var tokens = await verifyCtx.PushTokens.Where(pt => pt.UserId == userId).ToListAsync();
        tokens.Should().HaveCount(1);
        tokens[0].Platform.Should().Be(PushTokenPlatform.Android);
    }

    // ── RegisterTokenAsync: reassign token from another user ──

    [Fact]
    public async Task Register_Should_Reassign_Token_From_Another_User()
    {
        var user1 = SeedUser();
        var user2 = SeedUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.PushTokens.Add(new PushToken { UserId = user1, Token = "ExponentPushToken[shared]", Platform = PushTokenPlatform.Ios });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.RegisterTokenAsync(user2, "ExponentPushToken[shared]", "ios");

        using var verifyCtx = _dbFactory.CreateContext();
        var token = await verifyCtx.PushTokens.SingleAsync(pt => pt.Token == "ExponentPushToken[shared]");
        token.UserId.Should().Be(user2);
    }

    // ── RegisterTokenAsync: token cap enforcement ──

    [Fact]
    public async Task Register_Should_Remove_Oldest_Tokens_When_Exceeding_Cap()
    {
        var userId = SeedUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            for (int i = 0; i < 10; i++)
            {
                ctx.PushTokens.Add(new PushToken
                {
                    UserId = userId,
                    Token = $"ExponentPushToken[token{i}]",
                    Platform = PushTokenPlatform.Ios,
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-10 + i) // token0 is oldest
                });
            }
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[token_new]", "ios");

        using var verifyCtx = _dbFactory.CreateContext();
        var tokens = await verifyCtx.PushTokens.Where(pt => pt.UserId == userId).ToListAsync();
        tokens.Should().HaveCount(10);
        tokens.Should().NotContain(pt => pt.Token == "ExponentPushToken[token0]", "oldest token should be evicted");
        tokens.Should().Contain(pt => pt.Token == "ExponentPushToken[token_new]");
    }

    // ── RegisterTokenAsync: invalid platform ──

    [Fact]
    public async Task Register_Should_Throw_On_Invalid_Platform()
    {
        var userId = SeedUser();
        var svc = CreateService();

        var act = () => svc.RegisterTokenAsync(userId, "ExponentPushToken[t]", "windows");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── DeregisterTokenAsync ──

    [Fact]
    public async Task Deregister_Should_Remove_Token()
    {
        var userId = SeedUser();
        using (var ctx = _dbFactory.CreateContext())
        {
            ctx.PushTokens.Add(new PushToken { UserId = userId, Token = "ExponentPushToken[remove]", Platform = PushTokenPlatform.Android });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.DeregisterTokenAsync(userId, "ExponentPushToken[remove]");

        using var verifyCtx = _dbFactory.CreateContext();
        (await verifyCtx.PushTokens.AnyAsync(pt => pt.UserId == userId)).Should().BeFalse();
    }

    [Fact]
    public async Task Deregister_Should_Be_Idempotent()
    {
        var userId = SeedUser();
        var svc = CreateService();

        // Deregistering a non-existent token should not throw
        var act = () => svc.DeregisterTokenAsync(userId, "ExponentPushToken[nonexistent]");

        await act.Should().NotThrowAsync();
    }
}
