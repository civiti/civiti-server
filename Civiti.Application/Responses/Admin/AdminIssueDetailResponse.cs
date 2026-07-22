using Civiti.Domain.Entities;
using Civiti.Application.Responses.Authority;

namespace Civiti.Application.Responses.Admin;

/// <summary>
/// Detailed issue information for admin review, including user history and moderation data
/// </summary>
public class AdminIssueDetailResponse
{
    /// <summary>Unique issue identifier</summary>
    public Guid Id { get; set; }

    /// <summary>Issue title</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Full issue description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Issue category</summary>
    public IssueCategory Category { get; set; }

    /// <summary>Urgency level assigned by the reporter</summary>
    public UrgencyLevel Urgency { get; set; }

    /// <summary>Current moderation/lifecycle status</summary>
    public IssueStatus Status { get; set; }

    /// <summary>Street address of the issue</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>GPS latitude</summary>
    public double Latitude { get; set; }

    /// <summary>GPS longitude</summary>
    public double Longitude { get; set; }

    /// <summary>District or sector</summary>
    public string? District { get; set; }

    /// <summary>What the reporter hopes to achieve</summary>
    public string? DesiredOutcome { get; set; }

    /// <summary>Description of the community impact</summary>
    public string? CommunityImpact { get; set; }

    /// <summary>Admin notes from the review</summary>
    public string? AdminNotes { get; set; }

    /// <summary>Reason if the issue was rejected</summary>
    public string? RejectionReason { get; set; }

    /// <summary>When the issue was reviewed</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Admin who reviewed the issue</summary>
    public string? ReviewedBy { get; set; }

    /// <summary>When the issue was created</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the issue was last updated</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Internal ID of the reporting user</summary>
    public Guid UserId { get; set; }

    /// <summary>Display name of the reporting user</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Email of the reporting user</summary>
    public string UserEmail { get; set; } = string.Empty;

    /// <summary>Phone number of the reporting user</summary>
    public string? UserPhone { get; set; }

    /// <summary>Total issues reported by this user</summary>
    public int UserTotalIssues { get; set; }

    /// <summary>Resolved issues by this user</summary>
    public int UserResolvedIssues { get; set; }

    /// <summary>Gamification points of this user</summary>
    public int UserPoints { get; set; }

    /// <summary>Photos attached to the issue</summary>
    public List<AdminIssuePhotoResponse> Photos { get; set; } = [];

    /// <summary>Authorities linked to the issue</summary>
    public List<IssueAuthorityResponse> Authorities { get; set; } = [];

    /// <summary>Admin moderation action history</summary>
    public List<AdminActionResponse> AdminActions { get; set; } = [];

    /// <summary>Total emails sent for this issue</summary>
    public int EmailsSent { get; set; }

    /// <summary>
    /// The content as an admin last approved it, or <c>null</c> if this issue has never been
    /// approved.
    /// <para>
    /// <b>Null means "first review", not "nothing changed"</b> — the two must not render the
    /// same way. Use <see cref="ChangedFields"/> only when this is present.
    /// </para>
    /// </summary>
    public ApprovedIssueSnapshotResponse? ApprovedSnapshot { get; set; }

    /// <summary>
    /// Which fields differ from <see cref="ApprovedSnapshot"/>: any of <c>title</c>,
    /// <c>description</c>, <c>category</c>, <c>address</c>, <c>district</c>, <c>location</c>,
    /// <c>urgency</c>, <c>desiredOutcome</c>, <c>communityImpact</c>, <c>photos</c>,
    /// <c>authorities</c>.
    /// <para>
    /// Empty when the content is unchanged since approval, and also when there is no approved
    /// version to compare against — check <see cref="ApprovedSnapshot"/> to tell those apart.
    /// </para>
    /// </summary>
    public List<string> ChangedFields { get; set; } = [];
}

/// <summary>
/// An issue's content as an admin last approved it, for side-by-side comparison against the
/// version now awaiting review.
/// </summary>
public class ApprovedIssueSnapshotResponse
{
    /// <summary>When this content was approved.</summary>
    public DateTime ApprovedAt { get; set; }

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

    /// <summary>Photo URLs in their approved order — index 0 was the primary photo.</summary>
    public List<string> PhotoUrls { get; set; } = [];

    /// <summary>
    /// Authorities as they stood at approval, by value: a predefined authority may have been
    /// renamed since, and the comparison must reflect what the reviewer actually saw.
    /// </summary>
    public List<ApprovedIssueAuthorityResponse> Authorities { get; set; } = [];
}

/// <summary>An authority as recorded in an approved snapshot.</summary>
public class ApprovedIssueAuthorityResponse
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Photo attached to an issue, as seen by admins
/// </summary>
public class AdminIssuePhotoResponse
{
    /// <summary>Photo identifier</summary>
    public Guid Id { get; set; }

    /// <summary>Full-size photo URL</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Thumbnail URL for list views</summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>Optional description of the photo</summary>
    public string? Description { get; set; }

    /// <summary>Whether this is the primary/main photo</summary>
    public bool IsPrimary { get; set; }

    /// <summary>File size in bytes</summary>
    public int? FileSize { get; set; }

    /// <summary>When the photo was uploaded</summary>
    public DateTime CreatedAt { get; set; }
}
