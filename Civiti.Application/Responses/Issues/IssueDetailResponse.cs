using Civiti.Domain.Attributes;
using Civiti.Domain.Entities;
using Civiti.Application.Responses.Authority;

namespace Civiti.Application.Responses.Issues;

public class IssueDetailResponse
{
    public Guid Id { get; set; }
    [Untrusted] public string Title { get; set; } = string.Empty;
    [Untrusted] public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    [Untrusted] public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? District { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public IssueStatus Status { get; set; }
    public int EmailsSent { get; set; }
    public int CommunityVotes { get; set; }
    public bool? HasVoted { get; set; }
    [Untrusted] public string? DesiredOutcome { get; set; }
    [Untrusted] public string? CommunityImpact { get; set; }
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
    [Untrusted] public string? Description { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserBasicResponse
{
    /// <summary>
    /// The creator's Supabase auth id (the JWT <c>sub</c>) — the same identifier the caller
    /// holds for itself, so a client can compare it to decide ownership. For a deleted creator it
    /// is the all-zeros sentinel (<c>00000000-0000-0000-0000-000000000000</c>), which matches no
    /// caller. This is deliberately not the internal <c>UserProfile.Id</c> PK, which no client can
    /// match against its own identity.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    [Untrusted] public string Name { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
}