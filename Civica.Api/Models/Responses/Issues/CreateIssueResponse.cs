namespace Civica.Api.Models.Responses.Issues;

public class CreateIssueResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}