using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Application.Requests.Auth;
using Civiti.Domain.Entities;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Auth;
using Civiti.Application.Responses.Common;
using Civiti.Application.Responses.Gamification;
using Civiti.Application.Responses.Issues;
using Civiti.Application.Responses.User;
using Civiti.Application.Services;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Civiti.Api.Endpoints;

/// <summary>
/// User-specific endpoints for profile and personal data management
/// </summary>
public static class UserEndpoints
{
    private const int MaxDeleteAttemptsPerHour = 3;
    private static readonly TimeSpan DeleteRateLimitWindow = TimeSpan.FromHours(1);
    // TODO: IMemoryCache is per-process. If the app scales to multiple instances,
    // replace with IDistributedCache (Redis) or a gateway-level rate limiter.

    /// <summary>
    /// Thread-safe counter stored in IMemoryCache. Created once per key with a fixed
    /// absolute expiration; Interlocked.Increment ensures atomic read-modify-write.
    /// </summary>
    private sealed class RateLimitCounter
    {
        private int _count;
        public int Increment() => Interlocked.Increment(ref _count);
    }

    private static readonly object RateLimitLock = new();

    /// <summary>
    /// Returns true if the rate limit has been exceeded. The lock ensures only one
    /// RateLimitCounter is created per cache key (IMemoryCache.GetOrCreate is not
    /// atomic — concurrent misses can each invoke the factory). The Interlocked
    /// increment inside RateLimitCounter handles the actual counting thread-safely.
    /// </summary>
    private static bool IsDeleteRateLimited(IMemoryCache cache, string supabaseUserId)
    {
        string cacheKey = $"delete-cooldown:{supabaseUserId}";
        RateLimitCounter counter;
        lock (RateLimitLock)
        {
            counter = cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = DeleteRateLimitWindow;
                return new RateLimitCounter();
            })!;
        }
        return counter.Increment() > MaxDeleteAttemptsPerHour;
    }

    /// <summary>
    /// Maps user-related endpoints to the application
    /// </summary>
    /// <param name="app">The web application to map endpoints to</param>
    public static void MapUserEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.User.Base)
            .WithTags("User")
            .RequireAuthorization();

        // GET /api/user/profile
        group.MapGet(ApiRoutes.User.Profile, async (
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

            // Extract display name, photo, and signup metadata from JWT user_metadata
            var displayName = context.User.GetDisplayName(email);
            var photoUrl = context.User.GetPhotoUrl();
            var signupMetadata = context.User.GetSignupMetadata();

            // Get existing profile or auto-create one (signup metadata only used during creation)
            try
            {
                UserProfileResponse profile = await userService.GetOrCreateUserProfileAsync(
                    supabaseUserId, email, displayName, photoUrl, signupMetadata);
                return Results.Ok(profile);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("GetUserProfile")
        .WithSummary("Get user's complete profile")
        .WithDescription("Retrieves the complete profile for the authenticated user including personal information, gamification data (points, level, badges, achievements), and notification preferences. If no profile exists, one will be automatically created using data from the JWT token.")
        .Produces<UserProfileResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // POST /api/user/profile - Create or update profile
        group.MapPost(ApiRoutes.User.Profile, async (
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
                    UserProfileResponse updatedProfile = await userService.UpdateUserProfileAsync(supabaseUserId, request.ToUpdateRequest());
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
                try
                {
                    UserProfileResponse? concurrentProfile = await userService.GetUserProfileAsync(supabaseUserId);
                    if (concurrentProfile != null)
                    {
                        UserProfileResponse updatedProfile = await userService.UpdateUserProfileAsync(supabaseUserId, request.ToUpdateRequest());
                        return Results.Ok(updatedProfile);
                    }
                }
                catch (AccountDeletedException)
                {
                    return Results.Problem(
                        detail: DomainErrors.AccountDeleted,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account Deleted");
                }
                catch (InvalidOperationException ex) when (ex.Message == DomainErrors.UserNotFound)
                {
                    // Profile was deleted between get and update - very rare edge case
                    return Results.NotFound(new { error = "User profile not found" });
                }
                throw; // Re-throw if profile still doesn't exist (genuine DB error)
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
            catch (InvalidOperationException ex) when (ex.Message == DomainErrors.UserNotFound)
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
        .WithDescription("Creates a new user profile in the Civiti system after successful Supabase OAuth authentication, or updates an existing profile if one already exists. This endpoint is idempotent - calling it multiple times with the same data will not cause errors.")
        .Produces<UserProfileResponse>(StatusCodes.Status201Created)
        .Produces<UserProfileResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // PUT /api/user/profile
        group.MapPut(ApiRoutes.User.Profile, async (
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
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(new { error = "User profile not found" });
            }
        })
        .WithName("UpdateUserProfile")
        .WithSummary("Update user's profile")
        .WithDescription("Updates the authenticated user's profile information. Only provided fields will be updated; null fields are ignored. Returns the complete updated profile with gamification data.")
        .Produces<UserProfileResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/user/gamification
        group.MapGet(ApiRoutes.User.MyGamification, async (
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
                UserGamificationResponse? gamification = await userService.GetUserGamificationAsync(supabaseUserId);
                if (gamification == null)
                {
                    return Results.NotFound(new { error = DomainErrors.UserNotFound });
                }
                return Results.Ok(gamification);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("GetUserGamification")
        .WithSummary("Get user's gamification data")
        .Produces<UserGamificationResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/user/account/delete
        group.MapPost(ApiRoutes.User.AccountDelete, async (
            DeleteAccountRequest request,
            HttpContext context,
            IUserService userService,
            IMemoryCache memoryCache) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            if (IsDeleteRateLimited(memoryCache, supabaseUserId))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            try
            {
                var result = await userService.DeleteUserAsync(supabaseUserId);

                return result switch
                {
                    DeleteUserResult.NotFound => Results.NotFound(new { error = DomainErrors.UserNotFound }),
                    DeleteUserResult.Deleted => Results.NoContent(),
                    DeleteUserResult.AlreadyDeleted => Results.NoContent(),
                    _ => Results.NoContent()
                };
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(
                    detail: "An error occurred while deleting the account. Please try again later.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Delete Failed");
            }
        })
        .WithName("DeleteUserAccount")
        .WithSummary("Delete user account (soft delete)")
        .WithDescription("Permanently soft-deletes the authenticated user's account. Requires a JSON body with confirmation=\"DELETE\". All personal data is anonymized and the Supabase Auth account is removed (best-effort). The user's issues and comments are preserved with author shown as 'Deleted User'. This action cannot be undone. Rate limited to 3 attempts per hour.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status500InternalServerError);

        // GET /api/user/issues
        group.MapGet(ApiRoutes.User.MyIssues, async (
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
            if (!string.IsNullOrEmpty(status) && Enum.TryParse(status, true, out IssueStatus parsedStatus))
            {
                request.Status = parsedStatus;
            }

            try
            {
                PagedResult<IssueListResponse> issues = await issueService.GetUserIssuesAsync(supabaseUserId, request);
                return Results.Ok(issues);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
            catch (InvalidOperationException ex) when (ex.Message == DomainErrors.UserProfileNotFound)
            {
                return Results.Problem(
                    detail: DomainErrors.UserProfileNotFound,
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Profile Not Found");
            }
        })
        .WithName("GetUserIssues")
        .WithSummary("Get issues created by the authenticated user")
        .WithDescription("Returns a paginated list of issues created by the authenticated user. Returns 404 if the user's profile does not exist (e.g., token is valid but no Civiti profile was created). Returns 403 if the account has been soft-deleted.")
        .Produces<PagedResult<IssueListResponse>>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/user/leaderboard
        group.MapGet(ApiRoutes.User.Leaderboard, async (
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
        .Produces<LeaderboardResponse>();

        // PUT /api/user/issues/{id}/status
        group.MapPut(ApiRoutes.User.IssueStatus, async (
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
                    DomainErrors.AccountDeleted => Results.Problem(
                        detail: DomainErrors.AccountDeleted,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account Deleted"),
                    DomainErrors.IssueNotFound => Results.NotFound(new { error }),
                    DomainErrors.UserProfileNotFound => Results.NotFound(new { error }),
                    DomainErrors.ChangeOwnIssueStatusOnly => Results.Forbid(),
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
        group.MapPut(ApiRoutes.User.IssueById, async (
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

            (var success, IssueDetailResponse? issue, var error) = await issueService.UpdateIssueAsync(id, request, supabaseUserId);

            if (!success)
            {
                return error switch
                {
                    DomainErrors.AccountDeleted => Results.Problem(
                        detail: DomainErrors.AccountDeleted,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account Deleted"),
                    DomainErrors.IssueNotFound => Results.NotFound(new { error }),
                    DomainErrors.UserProfileNotFound => Results.NotFound(new { error }),
                    DomainErrors.EditOwnIssuesOnly => Results.Forbid(),
                    _ => Results.BadRequest(new { error })
                };
            }

            return Results.Ok(issue);
        })
        .WithName("UpdateUserIssue")
        .WithSummary("Update and resubmit an issue")
        .WithDescription("Allows the authenticated user to edit their own issue. Cannot edit Cancelled or Resolved issues. After editing, the issue status is set to 'UnderReview' for admin re-approval.")
        .Produces<IssueDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}
