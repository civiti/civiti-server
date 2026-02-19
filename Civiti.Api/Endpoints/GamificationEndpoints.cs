using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Models.Responses.Auth;
using Civiti.Api.Models.Responses.Gamification;
using Civiti.Api.Services.Interfaces;

namespace Civiti.Api.Endpoints;

/// <summary>
/// Gamification system endpoints for badges, achievements, and leaderboards
/// </summary>
public static class GamificationEndpoints
{
    /// <summary>
    /// Maps gamification-related endpoints to the application
    /// </summary>
    /// <param name="app">The web application to map endpoints to</param>
    public static void MapGamificationEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.Gamification.Base)
            .WithTags("Gamification");

        // GET /api/gamification/badges
        group.MapGet(ApiRoutes.Gamification.Badges, async (
            IGamificationService gamificationService) =>
        {
            List<BadgeResponse> badges = await gamificationService.GetAllBadgesAsync();
            return Results.Ok(badges);
        })
        .WithName("GetAllBadges")
        .WithSummary("Get all available badges")
        .WithDescription("Retrieves a list of all badges available in the gamification system, including their requirements and point values.")
        .Produces<List<BadgeResponse>>();

        // GET /api/gamification/badges/user
        group.MapGet(ApiRoutes.Gamification.BadgesUser, async (
            HttpContext context,
            IGamificationService gamificationService,
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

            List<BadgeResponse> badges = await gamificationService.GetAvailableBadgesAsync(profile.Id);
            return Results.Ok(badges);
        })
        .RequireAuthorization()
        .WithName("GetUserBadges")
        .WithSummary("Get all badges with user's earned status")
        .Produces<List<BadgeResponse>>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/gamification/achievements
        group.MapGet(ApiRoutes.Gamification.Achievements, async (
            IGamificationService gamificationService) =>
        {
            List<AchievementResponse> achievements = await gamificationService.GetAllAchievementsAsync();
            return Results.Ok(achievements);
        })
        .WithName("GetAllAchievements")
        .WithSummary("Get all available achievements")
        .Produces<List<AchievementResponse>>();

        // GET /api/gamification/achievements/user
        group.MapGet(ApiRoutes.Gamification.AchievementsUser, async (
            HttpContext context,
            IGamificationService gamificationService,
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

            List<AchievementProgressResponse> achievements = await gamificationService.GetUserAchievementsAsync(profile.Id);
            return Results.Ok(achievements);
        })
        .RequireAuthorization()
        .WithName("GetUserAchievements")
        .WithSummary("Get user's achievement progress")
        .Produces<List<AchievementProgressResponse>>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/gamification/leaderboard
        group.MapGet(ApiRoutes.Gamification.Leaderboard, async (
            string? period,
            string? category,
            int? limit,
            IGamificationService gamificationService) =>
        {
            var actualPeriod = period ?? "all";
            var actualCategory = category ?? "points";
            var actualLimit = Math.Min(limit ?? 50, 100); // Max 100

            LeaderboardResponse leaderboard = await gamificationService.GetLeaderboardAsync(actualPeriod, actualCategory, actualLimit);
            return Results.Ok(leaderboard);
        })
        .WithName("GetGamificationLeaderboard")
        .WithSummary("Get gamification leaderboard")
        .Produces<LeaderboardResponse>();
    }
}