using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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

            var email = context.User.GetEmail();
            if (string.IsNullOrEmpty(email))
            {
                return Results.BadRequest(new { error = "Email not found in token" });
            }

            // Extract display name and photo from JWT user_metadata
            var displayName = context.User.GetDisplayName(email);
            var photoUrl = context.User.GetPhotoUrl();

            // Get existing profile or auto-create one
            UserProfileResponse profile = await userService.GetOrCreateUserProfileAsync(
                supabaseUserId, email, displayName, photoUrl);

            return Results.Ok(profile);
        })
        .WithName("GetUserProfile")
        .WithSummary("Get user's complete profile")
        .WithDescription("Retrieves the complete profile for the authenticated user including personal information, gamification data (points, level, badges, achievements), and notification preferences. If no profile exists, one will be automatically created using data from the JWT token.")
        .Produces<UserProfileResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithOpenApi();

        // POST /api/user/profile - Create or update profile
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
                // Check if profile already exists
                UserProfileResponse? existingProfile = await userService.GetUserProfileAsync(supabaseUserId);
                if (existingProfile != null)
                {
                    // Profile exists - update it with provided data
                    UpdateUserProfileRequest updateRequest = new()
                    {
                        DisplayName = request.DisplayName,
                        PhotoUrl = request.PhotoUrl,
                        County = request.County,
                        City = request.City,
                        District = request.District,
                        ResidenceType = request.ResidenceType
                    };
                    UserProfileResponse updatedProfile = await userService.UpdateUserProfileAsync(supabaseUserId, updateRequest);
                    return Results.Ok(updatedProfile);
                }

                // Profile doesn't exist - attempt to create it
                UserProfileResponse profile = await userService.CreateUserProfileAsync(request, supabaseUserId, email);
                return Results.Created($"/api/user/profile", profile);
            }
            catch (DbUpdateException)
            {
                // Handle race condition: another request may have created the profile concurrently
                // Retry getting the profile and update it
                UserProfileResponse? concurrentProfile = await userService.GetUserProfileAsync(supabaseUserId);
                if (concurrentProfile != null)
                {
                    try
                    {
                        UpdateUserProfileRequest updateRequest = new()
                        {
                            DisplayName = request.DisplayName,
                            PhotoUrl = request.PhotoUrl,
                            County = request.County,
                            City = request.City,
                            District = request.District,
                            ResidenceType = request.ResidenceType
                        };
                        UserProfileResponse updatedProfile = await userService.UpdateUserProfileAsync(supabaseUserId, updateRequest);
                        return Results.Ok(updatedProfile);
                    }
                    catch (InvalidOperationException ex) when (ex.Message == "User not found")
                    {
                        // Profile was deleted between get and update - very rare edge case
                        return Results.NotFound(new { error = "User profile not found" });
                    }
                }
                throw; // Re-throw if profile still doesn't exist (genuine DB error)
            }
            catch (InvalidOperationException ex) when (ex.Message == "User not found")
            {
                // Profile was deleted between existence check and update
                return Results.NotFound(new { error = "User profile not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CreateUserProfile")
        .WithSummary("Create or update user profile")
        .WithDescription("Creates a new user profile in the Civica system after successful Supabase OAuth authentication, or updates an existing profile if one already exists. This endpoint is idempotent - calling it multiple times with the same data will not cause errors.")
        .Produces<UserProfileResponse>(StatusCodes.Status201Created)
        .Produces<UserProfileResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
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