namespace Civiti.Domain.Entities;

public class Achievement
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MaxProgress { get; set; } = 1;
    public int RewardPoints { get; set; } = 0;
    public Guid? RewardBadgeId { get; set; }
    public string AchievementType { get; set; } = string.Empty;
    public string? RequirementData { get; set; } // JSON data
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Badge? RewardBadge { get; set; }
    public List<UserAchievement> UserAchievements { get; set; } = [];
}