using System.ComponentModel.DataAnnotations;
using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Issues;

/// <summary>
/// Request model for updating an existing civic issue.
/// All fields are optional - only provided fields will be updated.
/// User can only update their own issues (except Cancelled or Resolved).
/// After update, status is set to UnderReview for admin re-approval.
/// </summary>
public class UpdateIssueRequest
{
    /// <summary>
    /// Brief, descriptive title of the issue (max 200 characters)
    /// </summary>
    [MinLength(1)]
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>
    /// Detailed description of the issue
    /// </summary>
    [MinLength(1)]
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Category of the civic issue
    /// </summary>
    public IssueCategory? Category { get; set; }

    /// <summary>
    /// Street address or location description
    /// </summary>
    [MinLength(1)]
    [MaxLength(500)]
    public string? Address { get; set; }

    /// <summary>
    /// District or sector name
    /// </summary>
    [MinLength(1)]
    [MaxLength(50)]
    public string? District { get; set; }

    /// <summary>
    /// Target authorities for this issue (replaces existing authorities)
    /// </summary>
    public List<IssueAuthorityInput>? Authorities { get; set; }

    /// <summary>
    /// Estimated number of people impacted
    /// </summary>
    [Range(1, 1000000)]
    public int? EstimatedImpact { get; set; }

    /// <summary>
    /// Tags for categorization and search
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// GPS latitude coordinate
    /// </summary>
    [Range(-90, 90)]
    public double? Latitude { get; set; }

    /// <summary>
    /// GPS longitude coordinate
    /// </summary>
    [Range(-180, 180)]
    public double? Longitude { get; set; }

    /// <summary>
    /// GPS location accuracy in meters
    /// </summary>
    [Range(1, 1000)]
    public int? LocationAccuracy { get; set; }

    /// <summary>
    /// Neighborhood or area name
    /// </summary>
    [MaxLength(100)]
    public string? Neighborhood { get; set; }

    /// <summary>
    /// Nearby landmark for easier identification
    /// </summary>
    [MaxLength(200)]
    public string? Landmark { get; set; }

    /// <summary>
    /// Urgency level of the issue
    /// </summary>
    public UrgencyLevel? Urgency { get; set; }

    /// <summary>
    /// Current situation description
    /// </summary>
    [MaxLength(2000)]
    public string? CurrentSituation { get; set; }

    /// <summary>
    /// Desired outcome or solution
    /// </summary>
    [MaxLength(1000)]
    public string? DesiredOutcome { get; set; }

    /// <summary>
    /// Impact on the community
    /// </summary>
    [MaxLength(1000)]
    public string? CommunityImpact { get; set; }

    /// <summary>
    /// AI-generated enhanced description (if used)
    /// </summary>
    [MaxLength(2000)]
    public string? AIGeneratedDescription { get; set; }

    /// <summary>
    /// AI-proposed solution (if available)
    /// </summary>
    [MaxLength(1000)]
    public string? AIProposedSolution { get; set; }

    /// <summary>
    /// AI confidence score (0-1) for generated content
    /// </summary>
    [Range(0, 1)]
    public decimal? AIConfidence { get; set; }

    /// <summary>
    /// URLs of uploaded photos (replaces existing photos, max 5)
    /// </summary>
    [MaxLength(5)]
    public List<string>? PhotoUrls { get; set; }
}
