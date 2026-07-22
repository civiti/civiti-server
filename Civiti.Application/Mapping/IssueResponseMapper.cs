using Civiti.Application.Responses.Authority;
using Civiti.Application.Responses.Issues;
using Civiti.Domain.Entities;

namespace Civiti.Application.Mapping;

/// <summary>
/// Projects a loaded <see cref="Issue"/> onto its public detail response.
/// <para>
/// Single implementation on purpose: the read and the edit paths used to carry a copy each,
/// and they had already drifted. Every field added to <see cref="IssueDetailResponse"/> now
/// gets populated once, for both.
/// </para>
/// </summary>
public static class IssueResponseMapper
{
    /// <summary>
    /// Requires <see cref="Issue.Photos"/>, <see cref="Issue.IssueAuthorities"/> (with their
    /// <see cref="IssueAuthority.Authority"/>) and <see cref="Issue.User"/> to be loaded.
    /// </summary>
    /// <param name="issue">The loaded issue.</param>
    /// <param name="hasVoted">
    /// Whether the viewer has voted, or <c>null</c> when voting does not apply (anonymous
    /// viewer, or the owner looking at their own issue).
    /// </param>
    public static IssueDetailResponse ToDetailResponse(Issue issue, bool? hasVoted) => new()
    {
        Id = issue.Id,
        Title = issue.Title,
        Description = issue.Description,
        Category = issue.Category,
        Address = issue.Address,
        Latitude = issue.Latitude,
        Longitude = issue.Longitude,
        District = issue.District,
        Urgency = issue.Urgency,
        Status = issue.Status,
        EmailsSent = issue.EmailsSent,
        CommunityVotes = issue.CommunityVotes,
        HasVoted = hasVoted,
        DesiredOutcome = issue.DesiredOutcome,
        CommunityImpact = issue.CommunityImpact,
        CreatedAt = issue.CreatedAt,
        UpdatedAt = issue.UpdatedAt,
        Photos = OrderPhotos(issue.Photos)
            .Select(p => new IssuePhotoResponse
            {
                Id = p.Id,
                Url = p.Url,
                Description = p.Description,
                IsPrimary = p.IsPrimary,
                CreatedAt = p.CreatedAt
            })
            .ToList(),
        Authorities = issue.IssueAuthorities
            .Select(ia => new IssueAuthorityResponse
            {
                AuthorityId = ia.AuthorityId,
                Name = ia.Authority?.Name ?? ia.CustomName ?? string.Empty,
                Email = ia.Authority?.Email ?? ia.CustomEmail ?? string.Empty,
                IsPredefined = ia.AuthorityId.HasValue
            })
            .ToList(),
        User = issue.User is not null
            ? new UserBasicResponse
            {
                Id = issue.User.Id,
                Name = issue.User.DisplayName,
                PhotoUrl = issue.User.PhotoUrl
            }
            : new UserBasicResponse
            {
                // The creator's profile row is gone (hard-deleted account); the issue itself is
                // preserved, attributed to nobody.
                Id = issue.UserId,
                Name = "Deleted User",
                PhotoUrl = null
            }
    };

    /// <summary>
    /// Deterministic photo order: primary first, then oldest first, with the id as a tiebreak.
    /// <para>
    /// Without an explicit order the database returns rows in whatever order it likes, which
    /// would make the client's "index 0 is the primary photo" convention fail to round-trip
    /// through an edit.
    /// </para>
    /// </summary>
    private static IEnumerable<IssuePhoto> OrderPhotos(IEnumerable<IssuePhoto> photos) =>
        photos
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id);
}
