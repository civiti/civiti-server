using Civiti.Api.Models.Responses.Gamification;

namespace Civiti.Api.Models.Responses.User;

public class UserGamificationResponse
{
    public int Points { get; set; }
    public int Level { get; set; }
    public int IssuesReported { get; set; }
    public int IssuesResolved { get; set; }
    public int CommunityVotes { get; set; }
    public int CurrentLoginStreak { get; set; }
    public int LongestLoginStreak { get; set; }
    public List<BadgeResponse> RecentBadges { get; set; } = [];
    public List<AchievementProgressResponse> ActiveAchievements { get; set; } = [];
    public int CurrentLevelPoints { get; set; }
    public int NextLevelPoints { get; set; }
    public int PointsToNextLevel { get; set; }
    public int PointsInCurrentLevel { get; set; }
    public double LevelProgressPercentage { get; set; }
}