using Civiti.Infrastructure.Data;
using Civiti.Domain.Entities;
using Civiti.Infrastructure.Services;
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
    private readonly List<CivitiDbContext> _contexts = [];

    public void Dispose()
    {
        _contexts.ForEach(c => c.Dispose());
        _dbFactory.Dispose();
    }

    private PushTokenService CreateService()
    {
        var ctx = _dbFactory.CreateContext();
        _contexts.Add(ctx);
        return new(ctx, _logger.Object);
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
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-(10 - i)) // token0 is oldest, token9 is newest
                });
            }
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[token_new]", "ios");

        using var verifyCtx = _dbFactory.CreateContext();
        var tokens = await verifyCtx.PushTokens.Where(pt => pt.UserId == userId).ToListAsync();
        tokens.Select(pt => pt.Token).Should().BeEquivalentTo(
            Enumerable.Range(1, 9).Select(i => $"ExponentPushToken[token{i}]")
                .Append("ExponentPushToken[token_new]"));
    }

    // ── RegisterTokenAsync: device-scoped replacement ──

    [Fact]
    public async Task Register_Should_Replace_Same_Device_Token_On_Rotation()
    {
        var userId = SeedUser();
        var svc = CreateService();

        await svc.RegisterTokenAsync(userId, "ExponentPushToken[old]", "ios", "device-A");
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[new]", "ios", "device-A");

        using var verifyCtx = _dbFactory.CreateContext();
        var tokens = await verifyCtx.PushTokens.Where(pt => pt.UserId == userId).ToListAsync();
        tokens.Should().ContainSingle();
        tokens[0].Token.Should().Be("ExponentPushToken[new]");
        tokens[0].DeviceId.Should().Be("device-A");
    }

    [Fact]
    public async Task Register_Should_Keep_Separate_Rows_For_Different_Devices()
    {
        var userId = SeedUser();
        var svc = CreateService();

        await svc.RegisterTokenAsync(userId, "ExponentPushToken[phone]", "ios", "device-A");
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[tablet]", "ios", "device-B");

        using var verifyCtx = _dbFactory.CreateContext();
        var tokens = await verifyCtx.PushTokens.Where(pt => pt.UserId == userId).ToListAsync();
        tokens.Select(t => t.Token).Should().BeEquivalentTo(
            "ExponentPushToken[phone]", "ExponentPushToken[tablet]");
    }

    [Fact]
    public async Task Register_Without_DeviceId_Should_Not_Collapse_Deviceless_Tokens()
    {
        var userId = SeedUser();
        var svc = CreateService();

        // Older clients send no device id — their rows must both survive...
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[a]", "ios");
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[b]", "ios");
        // ...and a blank device id must behave like "no device id", not collapse them.
        await svc.RegisterTokenAsync(userId, "ExponentPushToken[c]", "ios", "   ");

        using var verifyCtx = _dbFactory.CreateContext();
        var tokens = await verifyCtx.PushTokens.Where(pt => pt.UserId == userId).ToListAsync();
        tokens.Should().HaveCount(3);
        tokens.Should().OnlyContain(pt => pt.DeviceId == null);
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
