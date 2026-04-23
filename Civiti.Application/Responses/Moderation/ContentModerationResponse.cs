namespace Civiti.Application.Responses.Moderation;

public class ContentModerationResponse
{
    public bool IsAllowed { get; set; }
    public string? BlockReason { get; set; }
    public List<string> BlockedCategories { get; set; } = [];
}
