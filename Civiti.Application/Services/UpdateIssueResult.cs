using Civiti.Application.Responses.Issues;

namespace Civiti.Application.Services;

/// <summary>
/// Why an owner edit of an issue did or did not go through. Each member maps to exactly one
/// HTTP response, so the endpoint never has to infer intent from an error string.
/// </summary>
public enum UpdateIssueOutcome
{
    Success,
    IssueNotFound,
    UserProfileNotFound,
    AccountDeleted,

    /// <summary>Authenticated, but not the issue's creator.</summary>
    NotOwner,

    /// <summary>The issue's status does not permit owner edits (terminal or invalid).</summary>
    StatusNotEditable,

    /// <summary>The issue changed since the caller read it.</summary>
    ConcurrencyConflict,

    /// <summary>The submitted content failed a rule the request DTO cannot express alone.</summary>
    ValidationFailed
}

/// <summary>
/// Outcome of <see cref="IIssueService.UpdateIssueAsync"/>.
/// <see cref="Issue"/> is populated only when <see cref="Outcome"/> is
/// <see cref="UpdateIssueOutcome.Success"/>; <see cref="Error"/> only when it is not.
/// </summary>
public sealed record UpdateIssueResult(
    UpdateIssueOutcome Outcome,
    IssueDetailResponse? Issue = null,
    string? Error = null)
{
    public static UpdateIssueResult Ok(IssueDetailResponse issue) =>
        new(UpdateIssueOutcome.Success, issue);

    public static UpdateIssueResult Failed(UpdateIssueOutcome outcome, string error) =>
        new(outcome, null, error);
}
