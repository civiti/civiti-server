using Microsoft.AspNetCore.Authorization;
using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Extensions;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Models.Responses.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Civica.Api.Endpoints;

public static class IssueEndpoints
{
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
            GetIssuesRequest request = new GetIssuesRequest
            {
                Page = page ?? 1,
                PageSize = pageSize ?? 12,
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
        .Produces<PagedResult<IssueListResponse>>();

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
        .Produces<IssueDetailResponse>()
        .Produces(404);

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
        .Produces<CreateIssueResponse>()
        .Produces(400)
        .Produces(401);

        // PUT /api/issues/{id}/email-sent
        group.MapPut("/{id:guid}/email-sent", [Authorize] async Task<Results<Ok, BadRequest<string>, NotFound, UnauthorizedHttpResult>> (
            IIssueService issueService,
            Guid id,
            TrackEmailRequest request,
            HttpContext httpContext) =>
        {
            var supabaseUserId = httpContext.User.GetSupabaseUserId();
            
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return TypedResults.Unauthorized();
            }

            try
            {
                var success = await issueService.TrackEmailSentAsync(id, request, supabaseUserId);
                
                if (!success)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok();
            }
            catch (Exception ex)
            {
                return TypedResults.BadRequest(ex.Message);
            }
        })
        .WithName("TrackEmailSent")
        .WithSummary("Track that a user sent an email about an issue")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .Produces(401);
    }
}