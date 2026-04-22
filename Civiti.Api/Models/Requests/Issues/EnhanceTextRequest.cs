using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Entities;

namespace Civiti.Api.Models.Requests.Issues;

/// <summary>
/// Request model for AI-enhanced text generation for civic issues
/// </summary>
public class EnhanceTextRequest
{
    /// <summary>
    /// The original description of the civic issue to enhance
    /// </summary>
    /// <example>groapa mare pe strada, periculos pentru masini</example>
    [Required]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional desired outcome to enhance
    /// </summary>
    /// <example>sa fie reparat drumul</example>
    [MaxLength(1000)]
    public string? DesiredOutcome { get; set; }

    /// <summary>
    /// Optional community impact description to enhance
    /// </summary>
    /// <example>multi oameni trec pe aici zilnic</example>
    [MaxLength(1000)]
    public string? CommunityImpact { get; set; }

    /// <summary>
    /// Category of the issue for context
    /// </summary>
    [Required]
    public IssueCategory Category { get; set; }

    /// <summary>
    /// Optional location context for the enhancement
    /// </summary>
    /// <example>Strada Mihai Eminescu, Sector 2</example>
    [MaxLength(500)]
    public string? Location { get; set; }
}
