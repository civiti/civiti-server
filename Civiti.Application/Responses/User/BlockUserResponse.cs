namespace Civiti.Application.Responses.User;

public class BlockUserResponse
{
    public Guid BlockedUserId { get; set; }
    public DateTime BlockedAt { get; set; }
}
