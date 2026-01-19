using System.ComponentModel.DataAnnotations;
using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Issues;

/// <summary>
/// Request model for creating a new civic issue
/// </summary>
public class CreateIssueRequest
{
    /// <summary>
    /// Brief, descriptive title of the issue (max 200 characters)
    /// </summary>
    /// <example>Groapă periculoasă pe strada Mihai Eminescu</example>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Detailed description of the issue
    /// </summary>
    /// <example>O groapă adâncă de aproximativ 50cm s-a format în asfalt, reprezentând un pericol pentru vehicule și pietoni.</example>
    [Required]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Category of the civic issue
    /// </summary>
    [Required]
    public IssueCategory Category { get; set; }
    
    /// <summary>
    /// Street address or location description
    /// </summary>
    /// <example>Strada Mihai Eminescu, Nr. 45, Sector 2</example>
    [Required]
    [MaxLength(500)]
    public string Address { get; set; } = string.Empty;
    
    /// <summary>
    /// District or sector name
    /// </summary>
    /// <example>Sector 2</example>
    [Required]
    [MaxLength(50)]
    public string District { get; set; } = string.Empty;
    
    /// <summary>
    /// Target authorities for this issue (predefined or custom)
    /// </summary>
    public List<IssueAuthorityInput>? Authorities { get; set; }

    /// <summary>
    /// GPS latitude coordinate
    /// </summary>
    /// <example>44.4268</example>
    [Required]
    [Range(-90, 90)]
    public double Latitude { get; set; }
    
    /// <summary>
    /// GPS longitude coordinate
    /// </summary>
    /// <example>26.1025</example>
    [Required]
    [Range(-180, 180)]
    public double Longitude { get; set; }

    /// <summary>
    /// Urgency level of the issue (default: Medium)
    /// </summary>
    public UrgencyLevel Urgency { get; set; } = UrgencyLevel.Medium;

    /// <summary>
    /// Desired outcome or solution
    /// </summary>
    /// <example>Immediate repair of the road surface and proper drainage installation</example>
    [MaxLength(1000)]
    public string? DesiredOutcome { get; set; }
    
    /// <summary>
    /// Impact on the community
    /// </summary>
    /// <example>Affects approximately 500 residents daily, school children at risk</example>
    [MaxLength(1000)]
    public string? CommunityImpact { get; set; }

    /// <summary>
    /// URLs of uploaded photos (max 5 photos)
    /// </summary>
    /// <example>["https://storage.civica.ro/photos/issue-123-photo1.jpg"]</example>
    [MaxLength(5)]
    public List<string>? PhotoUrls { get; set; }
}

/// <summary>
/// Input model for linking an authority to an issue
/// </summary>
public class IssueAuthorityInput
{
    /// <summary>
    /// ID of a predefined authority. If provided, CustomName and CustomEmail should be null.
    /// </summary>
    public Guid? AuthorityId { get; set; }

    /// <summary>
    /// Custom authority name. Required if AuthorityId is not provided.
    /// </summary>
    [MaxLength(200)]
    public string? CustomName { get; set; }

    /// <summary>
    /// Custom authority email. Required if AuthorityId is not provided.
    /// </summary>
    [EmailAddress]
    [MaxLength(255)]
    public string? CustomEmail { get; set; }
}