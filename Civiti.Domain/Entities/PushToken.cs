namespace Civiti.Domain.Entities;

public class PushToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Stable per-installation identifier supplied by the mobile client. Lets a device
    /// replace its own token on rotation instead of accumulating orphaned rows. Null for
    /// clients that predate device-scoped registration.
    /// </summary>
    public string? DeviceId { get; set; }

    public PushTokenPlatform Platform { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public UserProfile User { get; set; } = null!;
}

public enum PushTokenPlatform
{
    Ios,
    Android
}
