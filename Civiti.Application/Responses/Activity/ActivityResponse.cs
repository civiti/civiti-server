using Civiti.Domain.Attributes;
using Civiti.Domain.Entities;

namespace Civiti.Application.Responses.Activity;

public class ActivityResponse
{
    public Guid Id { get; set; }
    public ActivityType Type { get; set; }
    public Guid IssueId { get; set; }
    [Untrusted] public string IssueTitle { get; set; } = string.Empty;
    [Untrusted] public string Message { get; set; } = string.Empty;
    public int AggregatedCount { get; set; }
    [Untrusted] public string? ActorDisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
}
