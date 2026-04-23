namespace Civiti.Domain.Entities;

public class UserBadge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BadgeId { get; set; }
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public UserProfile User { get; set; } = null!;
    public Badge Badge { get; set; } = null!;
}