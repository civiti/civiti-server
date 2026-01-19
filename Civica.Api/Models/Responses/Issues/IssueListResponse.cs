using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Responses.Issues;

public class IssueListResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public string Address { get; set; } = string.Empty;
    public UrgencyLevel Urgency { get; set; }
    public int EmailsSent { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? MainPhotoUrl { get; set; }
    public string? District { get; set; }
    public IssueStatus Status { get; set; }
}