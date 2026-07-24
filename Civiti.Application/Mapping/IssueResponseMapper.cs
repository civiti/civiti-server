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
        Photos = issue.Photos.InDisplayOrder()
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
                // The Supabase auth id (JWT sub), not the internal PK: it is the identifier the
                // client holds for itself, so its ownership check (issue.user.id === my sub) can
                // actually match. Mapping the PK here silently denied owners their own issues.
                Id = issue.User.SupabaseUserId,
                Name = issue.User.DisplayName,
                PhotoUrl = issue.User.PhotoUrl
            }
            : new UserBasicResponse
            {
                // The creator's profile row is gone (hard-deleted account); the issue itself is
                // preserved, attributed to nobody. No Supabase id survives, so leave it empty
                // rather than leak the internal FK — it matches no caller, which is correct.
                Id = string.Empty,
                Name = "Deleted User",
                PhotoUrl = null
            }
    };

}
