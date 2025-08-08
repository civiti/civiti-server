using Civica.Api.Models.Responses.User;

namespace Civica.Api.Models.Responses.Auth;

public class UserProfileResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string County { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string? ResidenceType { get; set; }
    public int Points { get; set; }
    public int Level { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool EmailVerified { get; set; }
    
    // Notification preferences
    public bool IssueUpdatesEnabled { get; set; }
    public bool CommunityNewsEnabled { get; set; }
    public bool MonthlyDigestEnabled { get; set; }
    public bool AchievementsEnabled { get; set; }
    
    // Gamification data
    public UserGamificationResponse? Gamification { get; set; }
}