using System.Diagnostics;

namespace Civiti.Api.Infrastructure.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString();
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Add request ID to response headers
        context.Response.Headers.Append("X-Request-Id", requestId);

        try
        {
            logger.LogInformation(
                "HTTP {Method} {Path} started. RequestId: {RequestId}",
                context.Request.Method,
                context.Request.Path,
                requestId
            );

            await next(context);

            stopwatch.Stop();

            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms. RequestId: {RequestId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                requestId
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            logger.LogError(
                ex,
                "HTTP {Method} {Path} failed after {ElapsedMilliseconds}ms. RequestId: {RequestId}",
                context.Request.Method,
                context.Request.Path,
                stopwatch.ElapsedMilliseconds,
                requestId
            );

            throw;
        }
    }
}