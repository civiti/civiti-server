using Civiti.Domain.Entities;

namespace Civiti.Domain.Policies;

/// <summary>
/// Which issue statuses their creator may edit, and what an edit does to the status.
/// Single source of truth for the service, the endpoint and the tests, so the state
/// machine cannot be restated (and mis-stated) per call site.
/// </summary>
public static class IssueEditPolicy
{
    /// <summary>
    /// Statuses whose owner may still edit the content.
    /// <para>
    /// <see cref="IssueStatus.Draft"/> is deliberately absent: no code path produces it
    /// (issue creation hard-codes <see cref="IssueStatus.Submitted"/>), so allowing it would
    /// be dead code. <see cref="IssueStatus.Resolved"/> and <see cref="IssueStatus.Cancelled"/>
    /// are terminal; <see cref="IssueStatus.Unspecified"/> is invalid.
    /// </para>
    /// <para>
    /// <see cref="IssueStatus.Active"/> is pending the admin re-review diff — editing a live,
    /// already-approved issue pulls it from public view and preserves its supporter counters,
    /// which is only safe once a reviewer can see what changed. See
    /// docs/technical/edit-issue-implementation-plan.md §4.4.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<IssueStatus> EditableStatuses =
    [
        IssueStatus.Rejected,
        IssueStatus.Submitted,
        IssueStatus.UnderReview
    ];

    public static bool IsEditable(IssueStatus status) => EditableStatuses.Contains(status);

    /// <summary>
    /// The status an issue lands in after its owner edits and resubmits it.
    /// <para>
    /// An issue already awaiting moderation keeps its status — it is in the queue, and demoting
    /// <see cref="IssueStatus.UnderReview"/> back to <see cref="IssueStatus.Submitted"/> would
    /// discard the signal that an admin has picked it up. Everything else lands on
    /// <see cref="IssueStatus.Submitted"/>, the same status a freshly created issue gets.
    /// </para>
    /// <para>
    /// Both are covered by the admin pending queue, which selects
    /// <see cref="IssueStatus.Submitted"/> ∪ <see cref="IssueStatus.UnderReview"/>.
    /// </para>
    /// </summary>
    public static IssueStatus ResolveStatusAfterResubmit(IssueStatus current) =>
        current is IssueStatus.Submitted or IssueStatus.UnderReview
            ? current
            : IssueStatus.Submitted;
}
