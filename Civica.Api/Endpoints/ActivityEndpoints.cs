using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Extensions;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Activity;
using Civica.Api.Models.Responses.Activity;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Endpoints;

/// <summary>
/// Activity feed endpoints for tracking recent events on issues
/// </summary>
public static class ActivityEndpoints
{
    /// <summary>
    /// Maps activity-related endpoints to the application
    /// </summary>
    /// <param name="app">The web application to map endpoints to</param>
    public static void MapActivityEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.Activity.Base)
            .WithTags("Activity")
            .WithOpenApi();

        // GET /api/activity - Public recent activity feed
        group.MapGet("", async (
            int? page,
            int? pageSize,
            ActivityType? type,
            DateTime? since,
            IActivityService activityService) =>
        {
            var request = new GetActivitiesRequest
            {
                Page = page ?? 1,
                PageSize = Math.Min(pageSize ?? 20, 100),
                Type = type,
                Since = since
            };

            PagedResult<ActivityResponse> result = await activityService.GetRecentActivitiesAsync(request);
            return Results.Ok(result);
        })
        .WithName("GetRecentActivities")
        .WithSummary("Get public recent activity feed")
        .WithDescription("Retrieves recent activity events for active, publicly visible issues. Includes new supporters, status changes, and approvals.")
        .Produces<PagedResult<ActivityResponse>>(StatusCodes.Status200OK)
        .WithOpenApi();

        // GET /api/activity/my - User's issue activities
        group.MapGet(ApiRoutes.Activity.My, async (
            HttpContext context,
            int? page,
            int? pageSize,
            ActivityType? type,
            DateTime? since,
            IActivityService activityService,
            IUserService userService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            UserProfileResponse? profile = await userService.GetUserProfileAsync(supabaseUserId);
            if (profile == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var request = new GetActivitiesRequest
            {
                Page = page ?? 1,
                PageSize = Math.Min(pageSize ?? 20, 100),
                Type = type,
                Since = since
            };

            PagedResult<ActivityResponse> result = await activityService.GetUserActivitiesAsync(profile.Id, request);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithName("GetUserActivities")
        .WithSummary("Get activities for user's issues")
        .WithDescription("Retrieves activity events for issues owned by the authenticated user. Useful for tracking engagement on your reported issues.")
        .Produces<PagedResult<ActivityResponse>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();
    }
}
