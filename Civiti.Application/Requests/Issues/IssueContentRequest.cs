using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Constants;
using Civiti.Domain.Entities;

namespace Civiti.Application.Requests.Issues;

/// <summary>
/// The editable content of a civic issue, shared by <see cref="CreateIssueRequest"/> and
/// <see cref="UpdateIssueRequest"/>.
/// <para>
/// Inheritance rather than duplication is deliberate: the two paths must never diverge, or an
/// owner ends up unable to save a value the create form happily accepted. Sharing the fields
/// and their attributes makes that parity structural instead of a review-time promise.
/// </para>
/// <para>
/// Required fields are declared as nullable CLR types carrying <see cref="RequiredAttribute"/>
/// so an omitted value fails validation with the field name attached, rather than binding
/// silently to <c>0.0</c> or to the first enum member.
/// </para>
/// </summary>
public abstract class IssueContentRequest
{
    /// <summary>
    /// Brief, descriptive title of the issue
    /// </summary>
    /// <example>Groapă periculoasă pe strada Mihai Eminescu</example>
    [Required]
    [MaxLength(IssueValidationLimits.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the issue
    /// </summary>
    /// <example>O groapă adâncă de aproximativ 50cm s-a format în asfalt, reprezentând un pericol pentru vehicule și pietoni.</example>
    [Required]
    [MinLength(IssueValidationLimits.MinDescriptionLength)]
    [MaxLength(IssueValidationLimits.MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category of the civic issue
    /// </summary>
    [Required]
    [EnumDataType(typeof(IssueCategory))]
    public IssueCategory? Category { get; set; }

    /// <summary>
    /// Street address or location description
    /// </summary>
    /// <example>Strada Mihai Eminescu, Nr. 45, Sector 2</example>
    [Required]
    [MaxLength(IssueValidationLimits.MaxAddressLength)]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// District or sector name
    /// </summary>
    /// <example>Sector 2</example>
    [Required]
    [MaxLength(IssueValidationLimits.MaxDistrictLength)]
    public string District { get; set; } = string.Empty;

    /// <summary>
    /// Target authorities for this issue (predefined or custom).
    /// On an edit this is the complete desired set, not a patch.
    /// </summary>
    [MaxLength(IssueValidationLimits.MaxAuthorityCount,
        ErrorMessage = "A maximum of {1} authorities are allowed.")]
    public List<IssueAuthorityInput>? Authorities { get; set; }

    /// <summary>
    /// GPS latitude coordinate
    /// </summary>
    /// <example>44.4268</example>
    [Required]
    [Range(IssueValidationLimits.MinLatitude, IssueValidationLimits.MaxLatitude)]
    public double? Latitude { get; set; }

    /// <summary>
    /// GPS longitude coordinate
    /// </summary>
    /// <example>26.1025</example>
    [Required]
    [Range(IssueValidationLimits.MinLongitude, IssueValidationLimits.MaxLongitude)]
    public double? Longitude { get; set; }

    /// <summary>
    /// Urgency level of the issue (default: Medium)
    /// </summary>
    [EnumDataType(typeof(UrgencyLevel))]
    public UrgencyLevel Urgency { get; set; } = UrgencyLevel.Medium;

    /// <summary>
    /// Desired outcome or solution
    /// </summary>
    /// <example>Immediate repair of the road surface and proper drainage installation</example>
    [MaxLength(IssueValidationLimits.MaxDesiredOutcomeLength)]
    public string? DesiredOutcome { get; set; }

    /// <summary>
    /// Impact on the community
    /// </summary>
    /// <example>Affects approximately 500 residents daily, school children at risk</example>
    [MaxLength(IssueValidationLimits.MaxCommunityImpactLength)]
    public string? CommunityImpact { get; set; }

    /// <summary>
    /// URLs of uploaded photos, ordered — index 0 becomes the primary photo.
    /// On an edit this is the complete desired set, not a patch.
    /// </summary>
    /// <example>["https://storage.civica.ro/photos/issue-123-photo1.jpg"]</example>
    [MaxLength(IssueValidationLimits.MaxPhotoCount, ErrorMessage = "A maximum of {1} photos are allowed.")]
    public List<string>? PhotoUrls { get; set; }
}

/// <summary>
/// Input model for linking an authority to an issue
/// </summary>
public class IssueAuthorityInput
{
    /// <summary>
    /// ID of a predefined authority. If provided, CustomName and CustomEmail must be null.
    /// </summary>
    public Guid? AuthorityId { get; set; }

    /// <summary>
    /// Custom authority name. Required if AuthorityId is not provided.
    /// </summary>
    [MaxLength(IssueValidationLimits.MaxAuthorityNameLength)]
    public string? CustomName { get; set; }

    /// <summary>
    /// Custom authority email. Required if AuthorityId is not provided.
    /// </summary>
    [EmailAddress]
    [MaxLength(IssueValidationLimits.MaxAuthorityEmailLength)]
    public string? CustomEmail { get; set; }
}
