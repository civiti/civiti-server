using Civica.Api.Models.Responses.Gamification;

namespace Civica.Api.Services.Interfaces;

public interface IGamificationService
{
    Task AwardPointsAsync(Guid userId, int points, string reason);
    Task CheckAndAwardBadgesAsync(Guid userId);
    Task CheckAndAwardAchievementsAsync(Guid userId);
    Task UpdateAchievementProgressAsync(Guid userId, string achievementType, int progress = 1);
    Task<List<BadgeResponse>> GetUserBadgesAsync(Guid userId);
    Task<List<AchievementProgressResponse>> GetUserAchievementsAsync(Guid userId);
    Task<List<BadgeResponse>> GetAvailableBadgesAsync(Guid userId);
    Task<List<BadgeResponse>> GetAllBadgesAsync();
    Task<List<AchievementResponse>> GetAllAchievementsAsync();
    Task<LeaderboardResponse> GetLeaderboardAsync(string period = "all", string category = "points", int limit = 50);
}