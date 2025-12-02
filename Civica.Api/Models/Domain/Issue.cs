namespace Civica.Api.Models.Domain;

public class Issue
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int LocationAccuracy { get; set; }
    public string? Neighborhood { get; set; }
    public string? District { get; set; } // District/Sector for Romanian administrative divisions
    public string? Landmark { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public int? EstimatedImpact { get; set; } // Estimated number of people affected
    public string? Tags { get; set; } // Comma-separated tags for categorization
    public IssueStatus Status { get; set; } = IssueStatus.Submitted;
    public int EmailsSent { get; set; } = 0;
    public string? CurrentSituation { get; set; }
    public string? DesiredOutcome { get; set; }
    public string? CommunityImpact { get; set; }
    public string? AIGeneratedDescription { get; set; }
    public string? AIProposedSolution { get; set; }
    public decimal? AIConfidence { get; set; }
    public string? AdminNotes { get; set; }
    public string? RejectionReason { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public string? AssignedDepartment { get; set; }
    public string? EstimatedResolutionTime { get; set; }
    public bool PublicVisibility { get; set; } = true;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public UserProfile User { get; set; } = null!;
    public List<IssuePhoto> Photos { get; set; } = [];
    public List<AdminAction> AdminActions { get; set; } = [];
    public List<IssueAuthority> IssueAuthorities { get; set; } = [];
}

/// <summary>
/// Categories for civic issues
/// </summary>
public enum IssueCategory
{
    /// <summary>Roads, sidewalks, bridges</summary>
    Infrastructure,
    /// <summary>Parks, pollution, waste</summary>
    Environment,
    /// <summary>Public transport, traffic</summary>
    Transportation,
    /// <summary>Utilities, government services</summary>
    PublicServices,
    /// <summary>Crime, lighting, hazards</summary>
    Safety,
    /// <summary>Uncategorized issues</summary>
    Other
}

/// <summary>
/// Urgency level of an issue
/// </summary>
public enum UrgencyLevel
{
    /// <summary>Not specified</summary>
    Unspecified = 0,
    /// <summary>Can be addressed in normal course</summary>
    Low = 1,
    /// <summary>Should be addressed soon</summary>
    Medium = 2,
    /// <summary>Requires prompt attention</summary>
    High = 3,
    /// <summary>Immediate action required</summary>
    Urgent = 4
}

/// <summary>
/// Current status of an issue in the workflow
/// </summary>
public enum IssueStatus
{
    /// <summary>Not specified</summary>
    Unspecified = 0,
    /// <summary>Saved as draft</summary>
    Draft = 1,
    /// <summary>Submitted for review</summary>
    Submitted = 2,
    /// <summary>Under admin review</summary>
    UnderReview = 3,
    /// <summary>Approved and public</summary>
    Approved = 4,
    /// <summary>Being worked on</summary>
    InProgress = 5,
    /// <summary>Issue resolved</summary>
    Resolved = 6,
    /// <summary>Rejected by admin</summary>
    Rejected = 7
}

/// <summary>
/// Admin-assigned priority level
/// </summary>
public enum Priority
{
    /// <summary>Not specified</summary>
    Unspecified = 0,
    /// <summary>Low priority</summary>
    Low = 1,
    /// <summary>Medium priority</summary>
    Medium = 2,
    /// <summary>High priority</summary>
    High = 3,
    /// <summary>Critical priority</summary>
    Critical = 4
}