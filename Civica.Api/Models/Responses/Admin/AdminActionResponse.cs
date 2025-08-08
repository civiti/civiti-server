using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Responses.Admin;

public class AdminActionResponse
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string IssueTitle { get; set; } = string.Empty;
    public Guid? AdminUserId { get; set; }
    public string AdminName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public AdminActionType ActionType { get; set; }
    public string? Notes { get; set; }
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? AssignedDepartment { get; set; }
    public string? EstimatedResolutionTime { get; set; }
    public DateTime CreatedAt { get; set; }
}