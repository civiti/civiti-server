using Civiti.Domain.Entities;

namespace Civiti.Api.Models.Responses.Activity;

public class ActivityResponse
{
    public Guid Id { get; set; }
    public ActivityType Type { get; set; }
    public Guid IssueId { get; set; }
    public string IssueTitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int AggregatedCount { get; set; }
    public string? ActorDisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
}
