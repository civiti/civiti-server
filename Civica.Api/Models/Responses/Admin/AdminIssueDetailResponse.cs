using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Responses.Admin;

public class AdminIssueDetailResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public Priority Priority { get; set; }
    public IssueStatus Status { get; set; }
    
    // Location details
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int LocationAccuracy { get; set; }
    public string? Neighborhood { get; set; }
    public string? District { get; set; }
    public string? Landmark { get; set; }
    public string? AuthorityEmail { get; set; }
    public int? EstimatedImpact { get; set; }
    public List<string>? Tags { get; set; }
    
    // Extended details
    public string? CurrentSituation { get; set; }
    public string? DesiredOutcome { get; set; }
    public string? CommunityImpact { get; set; }
    
    // AI analysis
    public string? AIGeneratedDescription { get; set; }
    public string? AIProposedSolution { get; set; }
    public decimal? AIConfidence { get; set; }
    
    // Admin details
    public string? AdminNotes { get; set; }
    public string? RejectionReason { get; set; }
    public string? AssignedDepartment { get; set; }
    public string? EstimatedResolutionTime { get; set; }
    public bool PublicVisibility { get; set; }
    
    // Review info
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // User info
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? UserPhone { get; set; }
    public int UserTotalIssues { get; set; }
    public int UserResolvedIssues { get; set; }
    public int UserPoints { get; set; }
    
    // Related data
    public List<AdminIssuePhotoResponse> Photos { get; set; } = [];
    public List<AdminActionResponse> AdminActions { get; set; } = [];
    public int EmailsSent { get; set; }
    public DateTime? LastEmailSentAt { get; set; }
}

public class AdminIssuePhotoResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Description { get; set; }
    public bool IsPrimary { get; set; }
    public int? FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
}