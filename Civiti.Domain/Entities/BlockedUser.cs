namespace Civiti.Domain.Entities;

public class BlockedUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public UserProfile User { get; set; } = null!;
    public UserProfile Blocked { get; set; } = null!;
}
