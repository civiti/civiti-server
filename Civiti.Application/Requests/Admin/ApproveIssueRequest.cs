using System.ComponentModel.DataAnnotations;

namespace Civiti.Application.Requests.Admin;

/// <summary>
/// Request to approve a submitted issue for public visibility
/// </summary>
public class ApproveIssueRequest
{
    /// <summary>
    /// Optional admin notes about the approval decision
    /// </summary>
    /// <example>Issue verified and approved for public visibility.</example>
    [MaxLength(2000)]
    public string? AdminNotes { get; set; }
}
