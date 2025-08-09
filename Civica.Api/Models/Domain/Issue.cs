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
    public string? Landmark { get; set; }
    public UrgencyLevel Urgency { get; set; }
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
    public List<EmailTracking> EmailTrackings { get; set; } = [];
}

public enum IssueCategory
{
    Infrastructure,
    Environment,
    Transportation,
    PublicServices,
    Safety,
    Other
}

public enum UrgencyLevel
{
    Unspecified = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4
}

public enum IssueStatus
{
    Unspecified = 0,
    Draft = 1,
    Submitted = 2,
    UnderReview = 3,
    Approved = 4,
    InProgress = 5,
    Resolved = 6,
    Rejected = 7
}

public enum Priority
{
    Unspecified = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}