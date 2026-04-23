using Civiti.Domain.Entities;

namespace Civiti.Application.Responses.Admin;

/// <summary>
/// Lightweight response for admin issue list views
/// </summary>
public class AdminIssueResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public IssueStatus Status { get; set; }
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int PhotoCount { get; set; }
    public int EmailsSent { get; set; }

    // Minimal user info
    public string UserName { get; set; } = string.Empty;
}
