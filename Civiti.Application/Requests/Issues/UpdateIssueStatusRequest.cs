using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Entities;

namespace Civiti.Application.Requests.Issues;

/// <summary>
/// Request model for updating an issue's status.
/// Users can only change status of their own issues.
/// </summary>
public class UpdateIssueStatusRequest
{
    /// <summary>
    /// The new status for the issue.
    /// Users can set: Cancelled, Resolved
    /// </summary>
    [Required]
    public IssueStatus Status { get; set; }
}
