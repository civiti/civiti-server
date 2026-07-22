using System.Text.Json;
using Civiti.Domain.Entities;
using Civiti.Domain.Snapshots;
using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Infrastructure.Services.Issues;

/// <summary>
/// Reads and writes the last-approved content snapshot for an issue.
/// <para>
/// Two write paths feed it. Approving an issue records what was approved; editing a live issue
/// captures its pre-edit content if nothing is on file yet, which is what gives a baseline to
/// every issue approved before this table existed — without a data migration that would have had
/// to build the same JSON in SQL and go live the moment it merged.
/// </para>
/// </summary>
internal static class IssueSnapshotStore
{
    /// <summary>
    /// Fixed serialisation settings. Pinned here rather than inherited from any ambient
    /// configuration: these bytes outlive the process that wrote them, so a change to the API's
    /// JSON options must not silently make existing rows unreadable.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    /// <summary>
    /// Records <paramref name="issue"/>'s current content as the approved version, replacing any
    /// previous snapshot. Stages the change on the context; the caller saves.
    /// </summary>
    public static async Task CaptureAsync(
        CivitiDbContext context,
        Issue issue,
        Guid? approvedByUserId,
        DateTime approvedAt,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(IssueContentSnapshot.From(issue), PayloadOptions);

        IssueApprovedSnapshot? existing = await context.IssueApprovedSnapshots
            .FirstOrDefaultAsync(s => s.IssueId == issue.Id, cancellationToken);

        if (existing == null)
        {
            context.IssueApprovedSnapshots.Add(new IssueApprovedSnapshot
            {
                IssueId = issue.Id,
                ApprovedAt = approvedAt,
                ApprovedByUserId = approvedByUserId,
                Payload = payload
            });

            return;
        }

        existing.ApprovedAt = approvedAt;
        existing.ApprovedByUserId = approvedByUserId;
        existing.Payload = payload;
    }

    /// <summary>
    /// Records the issue's content as approved only if nothing is on file. Used when an already
    /// publicly-visible issue is about to be edited: its current content <em>is</em> the approved
    /// content, and this is the last moment that is still true.
    /// </summary>
    public static async Task CaptureIfMissingAsync(
        CivitiDbContext context,
        Issue issue,
        DateTime approvedAt,
        CancellationToken cancellationToken = default)
    {
        var exists = await context.IssueApprovedSnapshots
            .AnyAsync(s => s.IssueId == issue.Id, cancellationToken);

        if (exists)
        {
            return;
        }

        // No approving admin is recorded: the approval predates this table, and inventing an
        // actor would be worse than admitting we do not know who it was.
        await CaptureAsync(context, issue, approvedByUserId: null, approvedAt, cancellationToken);
    }

    /// <summary>
    /// The approved content for an issue, or <c>null</c> when it has never been approved — which
    /// a reviewer must see as "first review", not as "nothing changed".
    /// </summary>
    public static async Task<(IssueContentSnapshot Content, DateTime ApprovedAt)?> TryReadAsync(
        CivitiDbContext context,
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        IssueApprovedSnapshot? snapshot = await context.IssueApprovedSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IssueId == issueId, cancellationToken);

        if (snapshot == null)
        {
            return null;
        }

        IssueContentSnapshot? content =
            JsonSerializer.Deserialize<IssueContentSnapshot>(snapshot.Payload, PayloadOptions);

        return content == null ? null : (content, snapshot.ApprovedAt);
    }
}
