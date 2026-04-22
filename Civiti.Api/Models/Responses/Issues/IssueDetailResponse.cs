using Civiti.Domain.Entities;
using Civiti.Api.Models.Responses.Authority;

namespace Civiti.Api.Models.Responses.Issues;

public class IssueDetailResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? District { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public IssueStatus Status { get; set; }
    public int EmailsSent { get; set; }
    public int CommunityVotes { get; set; }
    public bool? HasVoted { get; set; }
    public string? DesiredOutcome { get; set; }
    public string? CommunityImpact { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Related data
    public List<IssuePhotoResponse> Photos { get; set; } = [];
    public List<IssueAuthorityResponse> Authorities { get; set; } = [];
    public UserBasicResponse User { get; set; } = null!;
}

public class IssuePhotoResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserBasicResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
}