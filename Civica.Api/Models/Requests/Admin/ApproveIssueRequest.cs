using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Admin;

public class ApproveIssueRequest
{
    public string? AdminNotes { get; set; }
    public Priority? Priority { get; set; }
    public string? AssignedDepartment { get; set; }
    public string? EstimatedResolutionTime { get; set; }
}