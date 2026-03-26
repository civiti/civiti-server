namespace Civiti.Api.Models.Responses.User;

public class BlockUserResponse
{
    public Guid BlockedUserId { get; set; }
    public DateTime BlockedAt { get; set; }
}
