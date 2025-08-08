using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Admin;

public class BulkApproveRequest
{
    public List<Guid> IssueIds { get; set; } = [];
    public string? AdminNotes { get; set; }
    public Priority? DefaultPriority { get; set; }
    public string? DefaultDepartment { get; set; }
}