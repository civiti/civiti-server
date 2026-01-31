using System.ComponentModel.DataAnnotations;
using Civiti.Api.Models.Domain;

namespace Civiti.Api.Models.Requests.Issues;

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
    /// Urgency level of the issue
    /// </summary>
    public UrgencyLevel? Urgency { get; set; }

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
    /// URLs of uploaded photos (replaces existing photos, max 8)
    /// </summary>
    [MaxLength(8)]
    public List<string>? PhotoUrls { get; set; }
}
