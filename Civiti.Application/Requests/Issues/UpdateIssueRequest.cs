using System.ComponentModel.DataAnnotations;

namespace Civiti.Application.Requests.Issues;

/// <summary>
/// Request model for an owner editing their own civic issue.
/// <para>
/// The body is a <b>complete replacement</b> of the editable content, not a patch: every field
/// of <see cref="IssueContentRequest"/> must be supplied on every call, and
/// <see cref="IssueContentRequest.PhotoUrls"/> / <see cref="IssueContentRequest.Authorities"/>
/// are the complete desired sets. Accepting an all-optional partial would let a client that
/// simply omits a field silently blank a previously-set value.
/// </para>
/// <para>
/// A successful edit always sends the issue back for admin re-approval — see
/// <c>IssueEditPolicy</c> for the status transitions.
/// </para>
/// </summary>
public class UpdateIssueRequest : IssueContentRequest, IValidatableObject
{
    /// <summary>
    /// Acknowledges that saving sends the issue back to the moderation queue. Must be
    /// <c>true</c>.
    /// <para>
    /// There is no status in which a silent owner edit is legitimate: every editable status is
    /// either awaiting moderation already or has been publicly approved, and editing an
    /// approved issue without re-review is a moderation bypass (approve something benign, then
    /// quietly swap in spam). The flag is rejected rather than ignored so a client that means
    /// to skip re-approval learns that it cannot, instead of believing it succeeded.
    /// </para>
    /// </summary>
    /// <example>true</example>
    [Required]
    public bool? Resubmit { get; set; }

    /// <summary>
    /// The <c>updatedAt</c> the client last read for this issue — an optimistic-concurrency
    /// token. If it no longer matches the stored value the edit is rejected with
    /// <c>409 ISSUE_EDIT_CONFLICT</c> and nothing is written.
    /// <para>
    /// Without it, an owner sitting on a stale form silently overwrites whatever happened
    /// meanwhile — including an admin's approval or rejection.
    /// </para>
    /// </summary>
    /// <example>2026-07-21T09:00:00Z</example>
    [Required]
    public DateTime? ExpectedUpdatedAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Resubmit == false)
        {
            yield return new ValidationResult(
                "Editing an issue always sends it back for admin approval; 'resubmit' must be true.",
                [nameof(Resubmit)]);
        }
    }
}
