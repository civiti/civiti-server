using System.ComponentModel.DataAnnotations;

namespace Civiti.Api.Models.Requests.Admin;

/// <summary>
/// Request to approve multiple issues at once
/// </summary>
public class BulkApproveRequest
{
    /// <summary>
    /// List of issue IDs to approve
    /// </summary>
    [Required]
    public List<Guid> IssueIds { get; set; } = [];

    /// <summary>
    /// Optional admin notes applied to all approved issues
    /// </summary>
    /// <example>Batch approved after verification.</example>
    [MaxLength(2000)]
    public string? AdminNotes { get; set; }
}
