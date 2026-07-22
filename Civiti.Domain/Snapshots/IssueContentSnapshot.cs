using Civiti.Domain.Entities;

namespace Civiti.Domain.Snapshots;

/// <summary>
/// A point-in-time copy of an issue's reviewable content — everything a moderator's approval was
/// a judgement about. Serialised into <see cref="IssueApprovedSnapshot.Payload"/>.
/// <para>
/// Deliberately excludes anything the owner cannot change (status, supporter counters, the
/// creator) and anything a reviewer never sees. Comparing two snapshots therefore yields exactly
/// the set of fields the owner altered.
/// </para>
/// </summary>
public sealed class IssueContentSnapshot
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? District { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public string? DesiredOutcome { get; set; }
    public string? CommunityImpact { get; set; }

    /// <summary>Ordered as the owner arranged them — index 0 is the primary photo.</summary>
    public List<string> PhotoUrls { get; set; } = [];

    public List<IssueAuthoritySnapshot> Authorities { get; set; } = [];

    /// <summary>
    /// Builds a snapshot from a loaded issue. Requires <see cref="Issue.Photos"/> and
    /// <see cref="Issue.IssueAuthorities"/> (with their <see cref="IssueAuthority.Authority"/>)
    /// to be loaded.
    /// </summary>
    public static IssueContentSnapshot From(Issue issue) => new()
    {
        Title = issue.Title,
        Description = issue.Description,
        Category = issue.Category,
        Address = issue.Address,
        District = issue.District,
        Latitude = issue.Latitude,
        Longitude = issue.Longitude,
        Urgency = issue.Urgency,
        DesiredOutcome = issue.DesiredOutcome,
        CommunityImpact = issue.CommunityImpact,
        PhotoUrls = issue.Photos
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Select(p => p.Url)
            .ToList(),
        Authorities = issue.IssueAuthorities
            .Select(ia => new IssueAuthoritySnapshot
            {
                Name = ia.Authority?.Name ?? ia.CustomName ?? string.Empty,
                Email = ia.Authority?.Email ?? ia.CustomEmail ?? string.Empty
            })
            .ToList()
    };
}

/// <summary>
/// An authority as it stood at approval time, captured by value rather than by id: a predefined
/// authority can be renamed or have its address changed afterwards, and the diff must reflect
/// what the reviewer actually saw.
/// </summary>
public sealed class IssueAuthoritySnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
