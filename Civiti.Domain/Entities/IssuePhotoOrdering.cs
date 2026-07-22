namespace Civiti.Domain.Entities;

/// <summary>
/// The one true order for an issue's photos.
/// <para>
/// Shared by every read path — the public detail response, the admin detail response and the
/// approved-content snapshot — because they are compared against each other. If the snapshot
/// ordered photos differently from the response, re-submitting an untouched photo list would
/// register as a change and the re-review diff would cry wolf on the field a reviewer most needs
/// to trust.
/// </para>
/// </summary>
public static class IssuePhotoOrdering
{
    /// <summary>
    /// <see cref="IssuePhoto.DisplayOrder"/> first, which is authoritative for anything written
    /// since that column existed. The remaining keys only matter for older rows, where they
    /// reproduce the ordering those rows were displayed in before.
    /// </summary>
    public static IEnumerable<IssuePhoto> InDisplayOrder(this IEnumerable<IssuePhoto> photos) =>
        photos
            .OrderBy(p => p.DisplayOrder)
            .ThenByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id);
}
