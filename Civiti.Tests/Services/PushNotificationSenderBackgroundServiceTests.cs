using System.Net;
using System.Threading.Channels;
using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Push;
using Civiti.Api.Services;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Services;

public class PushNotificationSenderBackgroundServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<ILogger<PushNotificationSenderBackgroundService>> _logger = new();
    private readonly ExpoPushConfiguration _config = new() { BatchSize = 100 };

    public void Dispose() => _dbFactory.Dispose();

    private IServiceScopeFactory CreateScopeFactory()
    {
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(() =>
        {
            var scope = new Mock<IServiceScope>();
            var sp = new Mock<IServiceProvider>();
            sp.Setup(s => s.GetService(typeof(CivitiDbContext)))
                .Returns(_dbFactory.CreateContext());
            scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
            return scope.Object;
        });
        return factory.Object;
    }

    private (Guid userId, string token) SeedUserWithToken(bool pushEnabled = true)
    {
        using var db = _dbFactory.CreateContext();
        var user = TestDataBuilder.CreateUser();
        user.PushNotificationsEnabled = pushEnabled;
        db.UserProfiles.Add(user);

        var pushToken = new PushToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = $"ExponentPushToken[{Guid.NewGuid():N}]",
            Platform = PushTokenPlatform.Ios
        };
        db.PushTokens.Add(pushToken);
        db.SaveChanges();
        return (user.Id, pushToken.Token);
    }

    private async Task<int> RunServiceWithMessageAsync(
        PushNotificationMessage message, TestHttpHandler handler)
    {
        var channel = Channel.CreateUnbounded<PushNotificationMessage>();
        await channel.Writer.WriteAsync(message);
        channel.Writer.Complete();

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient("ExpoPush"))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new PushNotificationSenderBackgroundService(
            channel.Reader, CreateScopeFactory(), httpFactory.Object, _config, _logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        return handler.CallCount;
    }

    [Fact]
    public async Task Should_Skip_When_PushNotifications_Disabled()
    {
        var (userId, _) = SeedUserWithToken(pushEnabled: false);
        var handler = TestHttpHandler.AlwaysReturn(OkExpoResponse());

        int calls = await RunServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body"), handler);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task ForceSend_Should_Bypass_Preference_Check()
    {
        var (userId, _) = SeedUserWithToken(pushEnabled: false);
        var handler = TestHttpHandler.AlwaysReturn(OkExpoResponse());

        int calls = await RunServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body", ForceSend: true), handler);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Should_Remove_DeviceNotRegistered_Token()
    {
        var (userId, tokenValue) = SeedUserWithToken();
        var handler = TestHttpHandler.AlwaysReturn(DeviceNotRegisteredExpoResponse());

        await RunServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body"), handler);

        using var db = _dbFactory.CreateContext();
        db.PushTokens.Any(pt => pt.Token == tokenValue).Should().BeFalse();
    }

    [Fact]
    public async Task Should_Retry_Once_On_Http_Error_Then_Succeed()
    {
        var (userId, _) = SeedUserWithToken();
        int attempt = 0;
        var handler = new TestHttpHandler(_ =>
            Interlocked.Increment(ref attempt) == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("server error") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(OkExpoResponse()) });

        int calls = await RunServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body"), handler);

        calls.Should().Be(2);
    }

    private static string OkExpoResponse() =>
        """{"data":[{"status":"ok"}]}""";

    private static string DeviceNotRegisteredExpoResponse() =>
        """{"data":[{"status":"error","details":{"error":"DeviceNotRegistered"}}]}""";

    private class TestHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount;

        public static TestHttpHandler AlwaysReturn(string jsonBody) =>
            new(_ => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(jsonBody) });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult(factory(request));
        }
    }
}
