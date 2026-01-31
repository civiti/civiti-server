namespace Civiti.Api.Models.Requests.Admin;

public class BulkApproveRequest
{
    public List<Guid> IssueIds { get; set; } = [];
    public string? AdminNotes { get; set; }
}
