using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Responses.Admin;

public class AdminIssueResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public Priority Priority { get; set; }
    public IssueStatus Status { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? Neighborhood { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int PhotoCount { get; set; }
    public int EmailsSent { get; set; }
    
    // User info
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int UserTotalIssues { get; set; }
    
    // Review info
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? RejectionReason { get; set; }
    public string? AdminNotes { get; set; }
    
    // Assignment info
    public string? AssignedDepartment { get; set; }
    public string? EstimatedResolutionTime { get; set; }
    
    // Additional details
    public string? CurrentSituation { get; set; }
    public string? DesiredOutcome { get; set; }
    public string? CommunityImpact { get; set; }
    public bool PublicVisibility { get; set; }
    
    // AI analysis
    public string? AIGeneratedDescription { get; set; }
    public string? AIProposedSolution { get; set; }
    public decimal? AIConfidence { get; set; }
}