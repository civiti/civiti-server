namespace Civica.Api.Models.Requests.Issues;

public class TrackEmailRequest
{
    public string EmailAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? RecipientType { get; set; } = "authority";
    public string? AuthorityName { get; set; }
}