namespace Civiti.Application.Responses.Admin;

public class IssueActionResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Guid? IssueId { get; set; }
    public string? NewStatus { get; set; }
    public DateTime? UpdatedAt { get; set; }
}