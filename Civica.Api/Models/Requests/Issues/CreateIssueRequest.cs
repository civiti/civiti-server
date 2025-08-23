using System.ComponentModel.DataAnnotations;
using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Issues;

public class CreateIssueRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int LocationAccuracy { get; set; } = 10; // meters
    public string? Neighborhood { get; set; }
    public string? District { get; set; } // District/Sector for Romanian administrative divisions
    public string? Landmark { get; set; }
    public UrgencyLevel Urgency { get; set; } = UrgencyLevel.Medium;
    public string? AuthorityEmail { get; set; } // Target authority email address
    public string? EstimatedImpact { get; set; } // Estimated number of people affected
    public List<string>? Tags { get; set; } // Issue tags for categorization
    public string? CurrentSituation { get; set; }
    public string? DesiredOutcome { get; set; }
    public string? CommunityImpact { get; set; }
    public string? AIGeneratedDescription { get; set; }
    public string? AIProposedSolution { get; set; }
    public decimal? AIConfidence { get; set; }
    public List<string>? PhotoUrls { get; set; }
}