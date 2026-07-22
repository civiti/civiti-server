namespace Civiti.Application.Requests.Issues;

/// <summary>
/// Request model for creating a new civic issue.
/// Carries exactly the shared editable content — see <see cref="IssueContentRequest"/>.
/// </summary>
public class CreateIssueRequest : IssueContentRequest;
