using Civiti.Api.Models.Responses.Gamification;

namespace Civiti.Api.Services.Interfaces;

public interface IGamificationService
{
    Task FlushPendingNotificationsAsync();
    Task AwardPointsAsync(Guid userId, int points, string reason, bool saveChanges = true);
    Task DeductPointsAsync(Guid userId, int points, string reason, bool saveChanges = true);
    Task CheckAndAwardBadgesAsync(Guid userId, bool saveChanges = true);
    Task CheckAndAwardAchievementsAsync(Guid userId, bool saveChanges = true);
    Task UpdateAchievementProgressAsync(Guid userId, string achievementType, int progress = 1, bool isAbsolute = false, bool saveChanges = true);
    Task<List<BadgeResponse>> GetUserBadgesAsync(Guid userId);
    Task<List<AchievementProgressResponse>> GetUserAchievementsAsync(Guid userId);
    Task<List<BadgeResponse>> GetAvailableBadgesAsync(Guid userId);
    Task<List<BadgeResponse>> GetAllBadgesAsync();
    Task<List<AchievementResponse>> GetAllAchievementsAsync();
    Task<LeaderboardResponse> GetLeaderboardAsync(string period = "all", string category = "points", int limit = 50);
}