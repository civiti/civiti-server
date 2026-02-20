using System.ComponentModel.DataAnnotations;

namespace Civiti.Api.Models.Requests.Admin;

/// <summary>
/// Request to reject a submitted issue
/// </summary>
public class RejectIssueRequest
{
    /// <summary>
    /// Reason for rejection, shown to the issue author
    /// </summary>
    /// <example>Duplicate of existing issue #123. Please support the existing campaign instead.</example>
    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Internal admin notes not shown to the user
    /// </summary>
    [MaxLength(2000)]
    public string? InternalNotes { get; set; }
}
