using Microsoft.AspNetCore.Authorization;
using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Extensions;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Models.Responses.User;
using Civica.Api.Models.Responses.Gamification;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Endpoints;

/// <summary>
/// User-specific endpoints for profile and personal data management
/// </summary>
public static class UserEndpoints
{
    /// <summary>
    /// Maps user-related endpoints to the application
    /// </summary>
    /// <param name="app">The web application to map endpoints to</param>
    public static void MapUserEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.User.Base)
            .WithTags("User")
            .WithOpenApi()
            .RequireAuthorization();

        // GET /api/user/profile
        group.MapGet("/profile", async (
            HttpContext context,
            IUserService userService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            UserProfileResponse? profile = await userService.GetUserProfileAsync(supabaseUserId);
            
            return profile == null ? Results.NotFound(new { error = "User profile not found" }) : Results.Ok(profile);
        })
        .WithName("GetUserProfile")
        .WithSummary("Get user's complete profile")
        .WithDescription("Retrieves the complete profile for the authenticated user including personal information, gamification data, and notification preferences.")
        .Produces<UserProfileResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

        // PUT /api/user/profile
        group.MapPut("/profile", async (
            UpdateUserProfileRequest request,
            HttpContext context,
            IUserService userService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                UserProfileResponse updatedProfile = await userService.UpdateUserProfileAsync(supabaseUserId, request);
                return Results.Ok(updatedProfile);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(new { error = "User profile not found" });
            }
        })
        .WithName("UpdateUserProfile")
        .WithSummary("Update user's profile")
        .Produces<UserProfileResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/user/gamification
        group.MapGet(ApiRoutes.User.Gamification, async (
            HttpContext context,
            IUserService userService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            UserGamificationResponse gamification = await userService.GetUserGamificationAsync(supabaseUserId);
            return Results.Ok(gamification);
        })
        .WithName("GetUserGamification")
        .WithSummary("Get user's gamification data")
        .Produces<UserGamificationResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // DELETE /api/user/account
        group.MapDelete("/account", async (
            HttpContext context,
            IUserService userService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var deleted = await userService.DeleteUserAsync(supabaseUserId);
            
            return !deleted ? Results.NotFound(new { error = "User not found" }) : Results.NoContent();
        })
        .WithName("DeleteUserAccount")
        .WithSummary("Delete user account (soft delete)")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/user/issues
        group.MapGet("/issues", async (
            HttpContext context,
            IIssueService issueService,
            int? page,
            int? pageSize,
            string? status,
            string? sortBy,
            bool? sortDescending) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            GetUserIssuesRequest request = new()
            {
                Page = page ?? 1,
                PageSize = pageSize ?? 10,
                SortBy = sortBy ?? "date",
                SortDescending = sortDescending ?? true
            };

            // Parse status enum
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Civica.Api.Models.Domain.IssueStatus>(status, true, out IssueStatus parsedStatus))
            {
                request.Status = parsedStatus;
            }

            PagedResult<IssueListResponse> issues = await issueService.GetUserIssuesAsync(supabaseUserId, request);
            return Results.Ok(issues);
        })
        .WithName("GetUserIssues")
        .WithSummary("Get issues created by the authenticated user")
        .Produces<PagedResult<IssueListResponse>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/user/leaderboard
        group.MapGet("/leaderboard", async (
            int? page,
            int? pageSize,
            string? period,
            IUserService userService) =>
        {
            var actualPage = page ?? 1;
            var actualPageSize = Math.Min(pageSize ?? 50, 100); // Max 100 per page
            var actualPeriod = period ?? "all";

            LeaderboardResponse leaderboard = await userService.GetLeaderboardAsync(actualPage, actualPageSize, actualPeriod);
            return Results.Ok(leaderboard);
        })
        .AllowAnonymous() // Leaderboard is public
        .WithName("GetLeaderboard")
        .WithSummary("Get user leaderboard")
        .Produces<LeaderboardResponse>(StatusCodes.Status200OK);
    }
}