using Microsoft.AspNetCore.Authorization;
using Civica.Api.Services;
using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Extensions;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Models.Responses.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Civica.Api.Endpoints;

/// <summary>
/// Issue management and tracking endpoints
/// </summary>
public static class IssueEndpoints
{
    /// <summary>
    /// Maps issue-related endpoints to the application
    /// </summary>
    /// <param name="app">The web application to map endpoints to</param>
    public static void MapIssueEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.Issues.Base)
            .WithTags("Issues")
            .WithOpenApi();

        // GET /api/issues
        group.MapGet("/", async (
            IIssueService issueService,
            int? page,
            int? pageSize,
            string? category,
            string? urgency,
            string? district,
            string? sortBy,
            bool? sortDescending) =>
        {
            // Validate pagination parameters
            var actualPage = Math.Max(page ?? 1, 1);
            var actualPageSize = Math.Clamp(pageSize ?? 12, 1, 100);

            GetIssuesRequest request = new()
            {
                Page = actualPage,
                PageSize = actualPageSize,
                District = district,
                SortBy = sortBy ?? "date",
                SortDescending = sortDescending ?? true
            };

            // Parse category enum
            if (!string.IsNullOrEmpty(category) && Enum.TryParse<Civica.Api.Models.Domain.IssueCategory>(category, true, out IssueCategory parsedCategory))
            {
                request.Category = parsedCategory;
            }

            // Parse urgency enum
            if (!string.IsNullOrEmpty(urgency) && Enum.TryParse<Civica.Api.Models.Domain.UrgencyLevel>(urgency, true, out UrgencyLevel parsedUrgency))
            {
                request.Urgency = parsedUrgency;
            }

            PagedResult<IssueListResponse> result = await issueService.GetAllIssuesAsync(request);
            return Results.Ok(result);
        })
        .WithName("GetIssues")
        .WithSummary("Get paginated list of approved issues")
        .WithDescription("Retrieves a paginated list of approved civic issues. Supports filtering by category, urgency level, and district. Results can be sorted by date, popularity (email count), or urgency. This endpoint returns only issues that have been approved by administrators.")
        .Produces<PagedResult<IssueListResponse>>(200)
        .WithOpenApi(operation =>
        {
            operation.Parameters[0].Description = "Page number (default: 1)";
            operation.Parameters[1].Description = "Items per page (default: 12, max: 100)";
            operation.Parameters[2].Description = "Filter by category (Infrastructure, StreetLighting, GreenSpaces, etc.)";
            operation.Parameters[3].Description = "Filter by urgency (Low, Medium, High, Critical)";
            operation.Parameters[4].Description = "Filter by district (e.g., Sector 1, Sector 2)";
            operation.Parameters[5].Description = "Sort field (date, emails, urgency)";
            operation.Parameters[6].Description = "Sort in descending order (default: true)";
            return operation;
        });

        // GET /api/issues/{id}
        group.MapGet("/{id:guid}", async Task<Results<Ok<IssueDetailResponse>, NotFound>> (
            IIssueService issueService,
            Guid id) =>
        {
            IssueDetailResponse? issue = await issueService.GetIssueByIdAsync(id);
            
            return issue == null 
                ? TypedResults.NotFound() 
                : TypedResults.Ok(issue);
        })
        .WithName("GetIssueById")
        .WithSummary("Get issue details by ID")
        .WithDescription("Retrieves detailed information about a specific issue including full description, location data, photos, email tracking statistics, and related user information. Returns 404 if the issue doesn't exist or hasn't been approved yet.")
        .Produces<IssueDetailResponse>(200)
        .Produces(404)
        .WithOpenApi();

        // POST /api/issues
        group.MapPost("/", [Authorize] async Task<Results<Ok<CreateIssueResponse>, BadRequest<string>, UnauthorizedHttpResult>> (
            IIssueService issueService,
            CreateIssueRequest request,
            HttpContext httpContext) =>
        {
            var supabaseUserId = httpContext.User.GetSupabaseUserId();
            
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return TypedResults.Unauthorized();
            }

            try
            {
                CreateIssueResponse result = await issueService.CreateIssueAsync(request, supabaseUserId);
                return TypedResults.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(ex.Message);
            }
        })
        .WithName("CreateIssue")
        .WithSummary("Create a new issue (requires authentication)")
        .WithDescription("Creates a new civic issue report. The issue will be placed in pending status and requires admin approval before becoming publicly visible. Users earn gamification points for creating issues. Rate limited to 10 issues per hour per user to prevent spam.")
        .Produces<CreateIssueResponse>(201)
        .Produces(400)
        .Produces(401)
        .Produces(429)
        .WithOpenApi();

        // POST /api/issues/{id}/email-sent
        group.MapPost("/{id:guid}/email-sent", async Task<Results<Ok, BadRequest<string>, NotFound, StatusCodeHttpResult>> (
            IIssueService issueService,
            Guid id,
            HttpContext httpContext) =>
        {
            // Get client IP for rate limiting
            // Note: ForwardedHeaders middleware handles X-Forwarded-For, so RemoteIpAddress is already correct
            string? clientIp = httpContext.Connection.RemoteIpAddress?.ToString();

            var (success, error) = await issueService.IncrementEmailCountAsync(id, clientIp);

            if (!success)
            {
                if (error == "Issue not found")
                {
                    return TypedResults.NotFound();
                }

                if (error == IssueService.RateLimitedError)
                {
                    return TypedResults.StatusCode(429);
                }

                return TypedResults.BadRequest(error);
            }

            return TypedResults.Ok();
        })
        .WithName("ConfirmEmailSent")
        .WithSummary("Confirm that an email was sent about an issue")
        .WithDescription("Increments the email counter for this issue. This is a public endpoint (no authentication required) with rate limiting - each IP can only confirm once per issue per hour to prevent abuse.")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .Produces(429)
        .WithOpenApi();
    }
}