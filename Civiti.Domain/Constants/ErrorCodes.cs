namespace Civiti.Domain.Constants;

/// <summary>
/// Stable, machine-readable error codes returned alongside the human-readable message.
/// <para>
/// Needed wherever one HTTP status covers outcomes a client must react to differently —
/// the API's error messages are prose and must never be the discriminator.
/// </para>
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// 409 — the issue's status does not permit owner edits (terminal or invalid status).
    /// The client should stop offering the edit action for this issue.
    /// </summary>
    public const string IssueNotEditable = "ISSUE_NOT_EDITABLE";

    /// <summary>
    /// 409 — the issue changed since the client loaded it (<c>expectedUpdatedAt</c> mismatch).
    /// The client should reload the issue and let the owner reapply their edits.
    /// </summary>
    public const string IssueEditConflict = "ISSUE_EDIT_CONFLICT";
}
