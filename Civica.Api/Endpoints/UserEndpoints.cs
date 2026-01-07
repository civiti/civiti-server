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
        .WithDescription("Retrieves the complete profile for the authenticated user including personal information, gamification data (points, level, badges, achievements), and notification preferences. This is the primary endpoint for fetching user profile data.")
        .Produces<UserProfileResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

        // POST /api/user/profile - Create profile after OAuth registration
        group.MapPost("/profile", async (
            CreateUserProfileRequest request,
            HttpContext context,
            IUserService userService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var email = context.User.GetEmail();
            if (string.IsNullOrEmpty(email))
            {
                return Results.BadRequest(new { error = "Email not found in token" });
            }

            try
            {
                UserProfileResponse profile = await userService.CreateUserProfileAsync(request, supabaseUserId, email);
                return Results.Created($"/api/user/profile", profile);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CreateUserProfile")
        .WithSummary("Create user profile after OAuth registration")
        .WithDescription("Creates a new user profile in the Civica system after successful Supabase OAuth authentication. This endpoint should be called once per user immediately after their first login. The profile includes personal information, location details, and notification preferences.")
        .Produces<UserProfileResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
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
        .WithDescription("Updates the authenticated user's profile information. Only provided fields will be updated; null fields are ignored. Returns the complete updated profile with gamification data.")
        .Produces<UserProfileResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

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

            // Validate pagination parameters
            var actualPage = Math.Max(page ?? 1, 1);
            var actualPageSize = Math.Clamp(pageSize ?? 10, 1, 100);

            GetUserIssuesRequest request = new()
            {
                Page = actualPage,
                PageSize = actualPageSize,
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

        // PUT /api/user/issues/{id}/status
        group.MapPut("/issues/{id:guid}/status", async (
            Guid id,
            UpdateIssueStatusRequest request,
            HttpContext context,
            IIssueService issueService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var isAdmin = context.User.IsAdmin();
            var (success, error) = await issueService.UpdateIssueStatusAsync(id, request, supabaseUserId, isAdmin);

            if (!success)
            {
                return error switch
                {
                    "Issue not found" => Results.NotFound(new { error }),
                    "User profile not found" => Results.NotFound(new { error }),
                    "You can only change status of your own issues" => Results.Forbid(),
                    _ => Results.BadRequest(new { error })
                };
            }

            return Results.NoContent();
        })
        .WithName("UpdateUserIssueStatus")
        .WithSummary("Update issue status")
        .WithDescription("Allows the authenticated user to change their issue's status. Users can set status to: Cancelled, Resolved. Cannot change status of already cancelled or resolved issues.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // PUT /api/user/issues/{id}
        group.MapPut("/issues/{id:guid}", async (
            Guid id,
            UpdateIssueRequest request,
            HttpContext context,
            IIssueService issueService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var (success, issue, error) = await issueService.UpdateIssueAsync(id, request, supabaseUserId);

            if (!success)
            {
                return error switch
                {
                    "Issue not found" => Results.NotFound(new { error }),
                    "User profile not found" => Results.NotFound(new { error }),
                    "You can only edit your own issues" => Results.Forbid(),
                    _ => Results.BadRequest(new { error })
                };
            }

            return Results.Ok(issue);
        })
        .WithName("UpdateUserIssue")
        .WithSummary("Update and resubmit an issue")
        .WithDescription("Allows the authenticated user to edit their own issue. Cannot edit Cancelled or Resolved issues. After editing, the issue status is set to 'UnderReview' for admin re-approval.")
        .Produces<IssueDetailResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}