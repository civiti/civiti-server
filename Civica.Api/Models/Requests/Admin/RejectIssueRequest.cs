namespace Civica.Api.Models.Requests.Admin;

public class RejectIssueRequest
{
    public string Reason { get; set; } = string.Empty;
    public string? InternalNotes { get; set; }
}