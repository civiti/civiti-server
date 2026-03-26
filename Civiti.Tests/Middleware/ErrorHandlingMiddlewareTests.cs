using System.Net;
using System.Text.Json;
using Civiti.Api.Infrastructure.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Civiti.Tests.Middleware;

public class ErrorHandlingMiddlewareTests
{
    private readonly Mock<ILogger<ErrorHandlingMiddleware>> _logger = new();

    private ErrorHandlingMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, _logger.Object);

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/test";
        return context;
    }

    private static async Task<ErrorResponse> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ErrorResponse>(body,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
    }

    [Fact]
    public async Task Should_Pass_Through_When_No_Exception()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Should_Return_401_For_UnauthorizedAccessException()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        var body = await ReadResponseBody(context);
        body.Code.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Should_Return_404_For_KeyNotFoundException()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => throw new KeyNotFoundException("Not found"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        var body = await ReadResponseBody(context);
        body.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Should_Return_400_For_ArgumentException()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => throw new ArgumentException("Invalid argument"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        var body = await ReadResponseBody(context);
        body.Code.Should().Be("BAD_REQUEST");
        body.Error.Should().Be("Invalid argument");
    }

    [Fact]
    public async Task Should_Return_500_For_Unhandled_Exception_And_Mask_Details()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Sensitive DB error details"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        var body = await ReadResponseBody(context);
        body.Code.Should().Be("INTERNAL_ERROR");
        body.Error.Should().NotContain("Sensitive");
        body.Error.Should().Be("An error occurred while processing your request");
    }

    [Fact]
    public async Task Should_Set_ContentType_To_Json()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => throw new Exception("test"));

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task Should_Include_Request_Path_In_Response()
    {
        var context = CreateHttpContext();
        context.Request.Path = "/api/issues/123";
        var middleware = CreateMiddleware(_ => throw new Exception("test"));

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBody(context);
        body.Path.Should().Be("/api/issues/123");
    }
}
