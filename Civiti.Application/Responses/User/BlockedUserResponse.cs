namespace Civiti.Application.Responses.User;

public class BlockedUserResponse
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public DateTime BlockedAt { get; set; }
}
