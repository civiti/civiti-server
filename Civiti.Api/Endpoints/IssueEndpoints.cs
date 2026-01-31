using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Infrastructure.Filters;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Issues;
using Civiti.Api.Models.Responses.Auth;
using Civiti.Api.Models.Responses.Common;
using Civiti.Api.Models.Responses.Issues;
using Civiti.Api.Services;
using Civiti.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Civiti.Api.Endpoints;

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
            IUserService userService,
            HttpContext httpContext,
            int? page,
            int? pageSize,
            string? category,
            string? urgency,
            string? status,
            string? district,
            string? address,
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
                Address = address,
                SortBy = sortBy ?? "date",
                SortDescending = sortDescending ?? true
            };

            // Parse category enum
            if (!string.IsNullOrEmpty(category) && Enum.TryParse(category, true, out IssueCategory parsedCategory))
            {
                request.Category = parsedCategory;
            }

            // Parse urgency enum
            if (!string.IsNullOrEmpty(urgency) && Enum.TryParse(urgency, true, out UrgencyLevel parsedUrgency))
            {
                request.Urgency = parsedUrgency;
            }

            // Parse status enum(s) - supports comma-separated values (e.g., "Active,Resolved")
            if (!string.IsNullOrEmpty(status))
            {
                var statusParts = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                List<IssueStatus> parsedStatuses = [];

                foreach (var statusPart in statusParts)
                {
                    if (Enum.TryParse(statusPart, true, out IssueStatus parsedStatus))
                    {
                        parsedStatuses.Add(parsedStatus);
                    }
                }

                if (parsedStatuses.Count > 0)
                {
                    request.Statuses = parsedStatuses;
                }
            }

            // Get current user ID if authenticated (for HasVoted field)
            Guid? currentUserId = null;
            var supabaseUserId = httpContext.User.GetSupabaseUserId();
            if (!string.IsNullOrEmpty(supabaseUserId))
            {
                UserProfileResponse? userProfile = await userService.GetUserProfileAsync(supabaseUserId);
                currentUserId = userProfile?.Id;
            }

            PagedResult<IssueListResponse> result = await issueService.GetAllIssuesAsync(request, currentUserId);
            return Results.Ok(result);
        })
        .WithName("GetIssues")
        .WithSummary("Get paginated list of approved issues")
        .WithDescription("Retrieves a paginated list of civic issues. By default, returns only Active issues. Use the status filter to include Resolved issues. Supports filtering by category, urgency level, status, district, and address. Results can be sorted by date, popularity (email count), votes, or urgency. Only publicly visible issues are returned. If authenticated, includes HasVoted field indicating if the current user has voted on each issue.")
        .Produces<PagedResult<IssueListResponse>>()
        .WithOpenApi(operation =>
        {
            operation.Parameters[0].Description = "Page number (default: 1)";
            operation.Parameters[1].Description = "Items per page (default: 12, max: 100)";
            operation.Parameters[2].Description = "Filter by category (Infrastructure, StreetLighting, GreenSpaces, etc.)";
            operation.Parameters[3].Description = "Filter by urgency (Low, Medium, High, Critical)";
            operation.Parameters[4].Description = "Filter by status - comma-separated (e.g., 'Active,Resolved'). Default: Active only";
            operation.Parameters[5].Description = "Filter by district (e.g., Sector 1, Sector 2)";
            operation.Parameters[6].Description = "Filter by address (partial match, case-insensitive)";
            operation.Parameters[7].Description = "Sort field (date, emails, votes, urgency)";
            operation.Parameters[8].Description = "Sort in descending order (default: true)";
            return operation;
        });

        // GET /api/issues/{id}
        group.MapGet(ApiRoutes.Issues.ById, async Task<Results<Ok<IssueDetailResponse>, NotFound>> (
            IIssueService issueService,
            IUserService userService,
            HttpContext httpContext,
            Guid id) =>
        {
            // Get current user ID if authenticated (for HasVoted field)
            Guid? currentUserId = null;
            var supabaseUserId = httpContext.User.GetSupabaseUserId();
            if (!string.IsNullOrEmpty(supabaseUserId))
            {
                UserProfileResponse? userProfile = await userService.GetUserProfileAsync(supabaseUserId);
                currentUserId = userProfile?.Id;
            }

            IssueDetailResponse? issue = await issueService.GetIssueByIdAsync(id, currentUserId);

            return issue == null
                ? TypedResults.NotFound()
                : TypedResults.Ok(issue);
        })
        .WithName("GetIssueById")
        .WithSummary("Get issue details by ID")
        .WithDescription("Retrieves detailed information about a specific issue including full description, location data, photos, email tracking statistics, community votes, and related user information. If authenticated, includes HasVoted field indicating if the current user has voted on this issue. Returns 404 if the issue doesn't exist or hasn't been approved yet.")
        .Produces<IssueDetailResponse>()
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
        .AddEndpointFilter<ValidationFilter<CreateIssueRequest>>()
        .Produces<CreateIssueResponse>(201)
        .Produces(400)
        .Produces(401)
        .Produces(429)
        .WithOpenApi();

        // POST /api/issues/{id}/email-sent
        group.MapPost(ApiRoutes.Issues.EmailSent, async Task<Results<Ok, BadRequest<string>, NotFound, StatusCodeHttpResult>> (
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
                return error switch
                {
                    "Issue not found" => TypedResults.NotFound(),
                    IssueService.RateLimitedError => TypedResults.StatusCode(429),
                    _ => TypedResults.BadRequest(error)
                };
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

        // POST /api/issues/enhance-text
        group.MapPost(ApiRoutes.Issues.EnhanceText, [Authorize] async Task<Results<Ok<EnhanceTextResponse>, BadRequest<string>, UnauthorizedHttpResult, StatusCodeHttpResult>> (
            IClaudeEnhancementService enhancementService,
            IUserService userService,
            EnhanceTextRequest request,
            HttpContext httpContext) =>
        {
            var supabaseUserId = httpContext.User.GetSupabaseUserId();

            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return TypedResults.Unauthorized();
            }

            // Get internal user ID for rate limiting
            UserProfileResponse? userProfile = await userService.GetUserProfileAsync(supabaseUserId);
            if (userProfile == null)
            {
                return TypedResults.Unauthorized();
            }

            EnhanceTextResponse response = await enhancementService.EnhanceTextAsync(request, userProfile.Id);

            // Return 429 if rate limited (handled atomically in service)
            if (response.IsRateLimited)
            {
                return TypedResults.StatusCode(429);
            }

            return TypedResults.Ok(response);
        })
        .WithName("EnhanceText")
        .WithSummary("Enhance civic issue text using AI (requires authentication)")
        .WithDescription("Uses Claude AI to improve the quality, clarity, and professionalism of civic issue descriptions while preserving all original information. Returns enhanced text in Romanian. If AI enhancement fails, returns the original text with a warning. Rate limited to 10 requests per user per minute.")
        .Produces<EnhanceTextResponse>()
        .Produces(400)
        .Produces(401)
        .Produces(429)
        .WithOpenApi();

        // GET /api/issues/{id}/poster
        group.MapGet(ApiRoutes.Issues.Poster, async Task<Results<FileContentHttpResult, NotFound>> (
            IPosterService posterService,
            Guid id) =>
        {
            (byte[] PdfBytes, string FileName)? result = await posterService.GeneratePosterAsync(id);

            if (result == null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.File(
                result.Value.PdfBytes,
                contentType: "application/pdf",
                fileDownloadName: result.Value.FileName);
        })
        .WithName("GenerateIssuePoster")
        .WithSummary("Generate printable PDF poster with QR code")
        .WithDescription("Generates a printable A4 PDF poster featuring a QR code that links to the specified civic issue. The poster includes the Civiti branding, a large QR code, a Romanian call-to-action, and the issue title. Only available for publicly visible, active issues. No authentication required.")
        .Produces(200, contentType: "application/pdf")
        .Produces(404)
        .WithOpenApi();

        // POST /api/issues/{id}/vote
        group.MapPost(ApiRoutes.Issues.Vote, [Authorize] async Task<Results<Ok, BadRequest<string>, NotFound, UnauthorizedHttpResult>> (
            IIssueService issueService,
            HttpContext httpContext,
            Guid id) =>
        {
            var supabaseUserId = httpContext.User.GetSupabaseUserId();

            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return TypedResults.Unauthorized();
            }

            var (success, error) = await issueService.VoteForIssueAsync(id, supabaseUserId);

            if (!success)
            {
                return error switch
                {
                    "Issue not found" => TypedResults.NotFound(),
                    _ => TypedResults.BadRequest(error)
                };
            }

            return TypedResults.Ok();
        })
        .WithName("VoteForIssue")
        .WithSummary("Vote for an issue (requires authentication)")
        .WithDescription("Registers a community upvote for the specified issue. Users can only vote once per issue and cannot vote on their own issues. Only active issues can be voted on. Awards points to the issue author.")
        .Produces(200)
        .Produces(400)
        .Produces(401)
        .Produces(404)
        .WithOpenApi();

        // DELETE /api/issues/{id}/vote
        group.MapDelete(ApiRoutes.Issues.Vote, [Authorize] async Task<Results<Ok, BadRequest<string>, NotFound, UnauthorizedHttpResult>> (
            IIssueService issueService,
            HttpContext httpContext,
            Guid id) =>
        {
            var supabaseUserId = httpContext.User.GetSupabaseUserId();

            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return TypedResults.Unauthorized();
            }

            var (success, error) = await issueService.RemoveVoteAsync(id, supabaseUserId);

            if (!success)
            {
                return error switch
                {
                    "Issue not found" => TypedResults.NotFound(),
                    _ => TypedResults.BadRequest(error)
                };
            }

            return TypedResults.Ok();
        })
        .WithName("RemoveVoteFromIssue")
        .WithSummary("Remove vote from an issue (requires authentication)")
        .WithDescription("Removes a previously registered community upvote from the specified issue. Deducts points from the issue author.")
        .Produces(200)
        .Produces(400)
        .Produces(401)
        .Produces(404)
        .WithOpenApi();
    }
}