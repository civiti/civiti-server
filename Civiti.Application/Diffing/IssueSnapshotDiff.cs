using Civiti.Domain.Snapshots;

namespace Civiti.Application.Diffing;

/// <summary>
/// Names of the fields an admin re-review screen can highlight. Wire values, so they are
/// camelCase and match the corresponding response properties.
/// </summary>
public static class IssueDiffFields
{
    public const string Title = "title";
    public const string Description = "description";
    public const string Category = "category";
    public const string Address = "address";
    public const string District = "district";

    /// <summary>Latitude and longitude together — a coordinate is one thing to a reviewer.</summary>
    public const string Location = "location";

    public const string Urgency = "urgency";
    public const string DesiredOutcome = "desiredOutcome";
    public const string CommunityImpact = "communityImpact";
    public const string Photos = "photos";
    public const string Authorities = "authorities";
}

/// <summary>
/// Compares an issue's current content against the version an admin last approved.
/// <para>
/// Computed server-side rather than in the admin UI because the comparison has semantics a
/// client should not have to re-derive: null and empty mean the same thing, photo order is
/// significant while authority order is not, and coordinates are one field.
/// </para>
/// </summary>
public static class IssueSnapshotDiff
{
    /// <summary>
    /// Returns the fields that differ, in a stable order. An empty result means the content is
    /// unchanged since approval — which is not the same as there being no approved version to
    /// compare against; callers distinguish those by whether a snapshot exists at all.
    /// </summary>
    public static IReadOnlyList<string> Compare(IssueContentSnapshot approved, IssueContentSnapshot current)
    {
        List<string> changed = [];

        AddIf(changed, IssueDiffFields.Title, !TextEquals(approved.Title, current.Title));
        AddIf(changed, IssueDiffFields.Description, !TextEquals(approved.Description, current.Description));
        AddIf(changed, IssueDiffFields.Category, approved.Category != current.Category);
        AddIf(changed, IssueDiffFields.Address, !TextEquals(approved.Address, current.Address));
        AddIf(changed, IssueDiffFields.District, !TextEquals(approved.District, current.District));

        // Exact comparison, no tolerance. A tolerance would have to be chosen large enough to
        // absorb float noise from a map widget, and anything that large could also hide a small
        // but deliberate move. Over-reporting a coordinate change is cheap — the reviewer looks
        // and sees the same address — whereas under-reporting one defeats the point of the diff.
        AddIf(changed, IssueDiffFields.Location,
            approved.Latitude != current.Latitude || approved.Longitude != current.Longitude);

        AddIf(changed, IssueDiffFields.Urgency, approved.Urgency != current.Urgency);
        AddIf(changed, IssueDiffFields.DesiredOutcome, !TextEquals(approved.DesiredOutcome, current.DesiredOutcome));
        AddIf(changed, IssueDiffFields.CommunityImpact, !TextEquals(approved.CommunityImpact, current.CommunityImpact));

        // Ordered: index 0 is the primary photo, so a reorder changes what the public sees.
        AddIf(changed, IssueDiffFields.Photos,
            !approved.PhotoUrls.SequenceEqual(current.PhotoUrls, StringComparer.Ordinal));

        AddIf(changed, IssueDiffFields.Authorities,
            !AuthoritiesEqual(approved.Authorities, current.Authorities));

        return changed;
    }

    private static void AddIf(List<string> target, string field, bool changed)
    {
        if (changed)
        {
            target.Add(field);
        }
    }

    /// <summary>
    /// Null and empty are the same absence of a value; a legacy issue with a null district that
    /// the owner leaves blank has not changed anything.
    /// </summary>
    private static bool TextEquals(string? left, string? right) =>
        string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right)
        || string.Equals(left, right, StringComparison.Ordinal);

    /// <summary>
    /// Order-insensitive, unlike photos: which authorities an issue targets is meaningful, the
    /// sequence they are listed in is not. Emails are compared case-insensitively because that
    /// is how the write path de-duplicates them.
    /// </summary>
    private static bool AuthoritiesEqual(
        List<IssueAuthoritySnapshot> approved,
        List<IssueAuthoritySnapshot> current)
    {
        if (approved.Count != current.Count)
        {
            return false;
        }

        // Compared as tuples rather than as concatenated keys: any separator character could
        // also occur inside a name, letting ("Primăria X", "a@b") and ("Primăria", "X a@b")
        // collide into one key and hide a redirected recipient.
        //
        // Emails are folded with OrdinalIgnoreCase rather than ToLowerInvariant. Invariant
        // lowercasing applies full Unicode case mapping, which folds distinct code points onto
        // ASCII — U+212A KELVIN SIGN becomes "k" — so "Kontakt@ps2.ro" written with a Kelvin
        // sign would compare equal to "kontakt@ps2.ro" while being a different mailbox as far
        // as the mail system is concerned. Ordinal folding leaves U+212A alone, so the
        // substitution is reported. That is the exact shape of the attack this diff exists to
        // catch: a recipient swap that looks identical to a reviewer.
        static List<(string Name, string Email)> Normalize(List<IssueAuthoritySnapshot> authorities) =>
            authorities
                .Select(a => (Name: a.Name.Trim(), Email: a.Email.Trim()))
                .OrderBy(a => a.Name, StringComparer.Ordinal)
                .ThenBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
                .ToList();

        List<(string Name, string Email)> left = Normalize(approved);
        List<(string Name, string Email)> right = Normalize(current);

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal)
                || !string.Equals(left[i].Email, right[i].Email, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
