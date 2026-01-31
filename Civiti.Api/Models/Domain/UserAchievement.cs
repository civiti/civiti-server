namespace Civiti.Api.Models.Domain;

public class UserAchievement
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AchievementId { get; set; }
    public int Progress { get; set; } = 0;
    public bool Completed { get; set; } = false;
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public UserProfile User { get; set; } = null!;
    public Achievement Achievement { get; set; } = null!;
}