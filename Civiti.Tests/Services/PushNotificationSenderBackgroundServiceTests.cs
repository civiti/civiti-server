using System.Net;
using System.Threading.Channels;
using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Domain.Entities;
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

    /// <summary>
    /// Starts the service, writes a single message, and waits deterministically.
    /// For tests expecting HTTP calls, waits for the handler to be invoked
    /// <paramref name="expectedCalls"/> times. Then awaits <c>ExecuteTask</c> so all
    /// post-HTTP processing (e.g. DB deletes) completes before returning.
    /// </summary>
    private async Task<int> StartServiceWithMessageAsync(
        PushNotificationMessage message, TestHttpHandler handler, int expectedCalls = 0)
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

        if (expectedCalls > 0)
            await handler.WaitForCallsAsync(expectedCalls, TimeSpan.FromSeconds(3));

        // The service exits its loop when the channel completes, so ExecuteTask
        // resolves after all in-flight processing (including post-HTTP DB work)
        // finishes — no cancellation needed, no timing assumptions.
        service.ExecuteTask.Should().NotBeNull("service should have been started");
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        return handler.CallCount;
    }

    [Fact]
    public async Task Should_Skip_When_PushNotifications_Disabled()
    {
        var (userId, _) = SeedUserWithToken(pushEnabled: false);
        var handler = TestHttpHandler.AlwaysReturn(OkExpoResponse());

        var calls = await StartServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body"), handler);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task ForceSend_Should_Bypass_Preference_Check()
    {
        var (userId, _) = SeedUserWithToken(pushEnabled: false);
        var handler = TestHttpHandler.AlwaysReturn(OkExpoResponse());

        var calls = await StartServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body", ForceSend: true), handler,
            expectedCalls: 1);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Should_Remove_DeviceNotRegistered_Token()
    {
        var (userId, tokenValue) = SeedUserWithToken();
        var handler = TestHttpHandler.AlwaysReturn(DeviceNotRegisteredExpoResponse());

        await StartServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body"), handler,
            expectedCalls: 1);

        // Service is already stopped — all processing is complete
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

        var calls = await StartServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body"), handler,
            expectedCalls: 2);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Should_Log_Error_And_Drop_When_Both_Attempts_Fail()
    {
        var (userId, _) = SeedUserWithToken();
        var handler = new TestHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
                { Content = new StringContent("server error") });

        var calls = await StartServiceWithMessageAsync(
            new PushNotificationMessage(userId, "Title", "Body"), handler,
            expectedCalls: 2);

        calls.Should().Be(2);

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to send push batch")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static string OkExpoResponse() =>
        """{"data":[{"status":"ok"}]}""";

    private static string DeviceNotRegisteredExpoResponse() =>
        """{"data":[{"status":"error","details":{"error":"DeviceNotRegistered"}}]}""";

    private class TestHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        private int _callCount;
        private TaskCompletionSource? _tcs;
        private int _targetCalls;

        public int CallCount => Volatile.Read(ref _callCount);

        public static TestHttpHandler AlwaysReturn(string jsonBody) =>
            new(_ => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(jsonBody) });

        public Task WaitForCallsAsync(int count, TimeSpan timeout)
        {
            _targetCalls = count;
            _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (Volatile.Read(ref _callCount) >= count)
                _tcs.TrySetResult();

            return _tcs.Task.WaitAsync(timeout);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var result = factory(request);
            if (Interlocked.Increment(ref _callCount) >= _targetCalls)
                _tcs?.TrySetResult();
            return Task.FromResult(result);
        }
    }
}
