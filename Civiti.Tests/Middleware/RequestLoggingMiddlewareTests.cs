using Civiti.Api.Infrastructure.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Middleware;

public class RequestLoggingMiddlewareTests
{
    private readonly Mock<ILogger<RequestLoggingMiddleware>> _logger = new();

    private RequestLoggingMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, _logger.Object);

    [Fact]
    public async Task Should_Add_XRequestId_Header()
    {
        var context = new DefaultHttpContext();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-Request-Id");
        context.Response.Headers["X-Request-Id"].ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task XRequestId_Should_Be_Valid_Guid()
    {
        var context = new DefaultHttpContext();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var requestId = context.Response.Headers["X-Request-Id"].ToString();
        Guid.TryParse(requestId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Should_Log_Start_And_Completion()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/test";
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        // Verify two Information-level log calls were made (start + completion)
        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Should_Rethrow_Exception_After_Logging()
    {
        var context = new DefaultHttpContext();
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("test"));

        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("test");
    }

    [Fact]
    public async Task Should_Log_Error_On_Exception()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/issues";
        var middleware = CreateMiddleware(_ => throw new Exception("fail"));

        try { await middleware.InvokeAsync(context); } catch { /* expected */ }

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Still_Add_XRequestId_Even_On_Exception()
    {
        var context = new DefaultHttpContext();
        var middleware = CreateMiddleware(_ => throw new Exception("fail"));

        try { await middleware.InvokeAsync(context); } catch { /* expected */ }

        context.Response.Headers.Should().ContainKey("X-Request-Id");
    }
}
