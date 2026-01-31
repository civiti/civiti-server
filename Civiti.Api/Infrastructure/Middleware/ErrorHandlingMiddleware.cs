using System.Net;
using System.Text.Json;
using FluentValidation;

namespace Civiti.Api.Infrastructure.Middleware;

public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        HttpResponse response = context.Response;
        response.ContentType = "application/json";

        ErrorResponse errorResponse = new()
        {
            Timestamp = DateTime.UtcNow,
            Path = context.Request.Path
        };

        switch (exception)
        {
            case ValidationException validationException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Error = "Validation failed";
                errorResponse.Code = "VALIDATION_ERROR";
                errorResponse.Details = validationException.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                break;
                
            case UnauthorizedAccessException _:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Error = "Unauthorized access";
                errorResponse.Code = "UNAUTHORIZED";
                break;
                
            case KeyNotFoundException _:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.Error = "Resource not found";
                errorResponse.Code = "NOT_FOUND";
                break;
                
            case ArgumentException argumentException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Error = argumentException.Message;
                errorResponse.Code = "BAD_REQUEST";
                break;
                
            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.Error = "An error occurred while processing your request";
                errorResponse.Code = "INTERNAL_ERROR";
                
                logger.LogError(exception, "Unhandled exception occurred");
                break;
        }

        JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var errorJson = JsonSerializer.Serialize(errorResponse, jsonOptions);
        
        await response.WriteAsync(errorJson);
    }
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string[]>? Details { get; set; }
}