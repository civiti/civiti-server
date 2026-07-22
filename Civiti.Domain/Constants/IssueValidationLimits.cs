namespace Civiti.Domain.Constants;

/// <summary>
/// Field limits shared by every issue write path — create, owner edit and the MCP tools.
/// Centralised so validation parity holds by construction: an edit must never reject a
/// value that create accepted, or vice-versa.
/// </summary>
public static class IssueValidationLimits
{
    public const int MaxTitleLength = 200;

    /// <summary>
    /// A description shorter than this carries no reviewable content. Applied to create and
    /// edit alike; issues predating this rule must lengthen their description on first edit.
    /// </summary>
    public const int MinDescriptionLength = 10;
    public const int MaxDescriptionLength = 2000;

    public const int MaxAddressLength = 500;
    public const int MaxDistrictLength = 50;
    public const int MaxDesiredOutcomeLength = 1000;
    public const int MaxCommunityImpactLength = 1000;

    public const int MaxPhotoCount = 8;

    /// <summary>Matches the width used for other user-supplied URLs (profile photos).</summary>
    public const int MaxPhotoUrlLength = 1000;

    /// <summary>
    /// No minimum is enforced: the MCP <c>create_issue</c> tool takes no authorities argument,
    /// so issues legitimately exist with none, and a server-side minimum would lock their
    /// owners out of editing. Requiring at least one is a client-side rule.
    /// </summary>
    public const int MaxAuthorityCount = 5;
    public const int MaxAuthorityNameLength = 200;
    public const int MaxAuthorityEmailLength = 255;

    public const double MinLatitude = -90;
    public const double MaxLatitude = 90;
    public const double MinLongitude = -180;
    public const double MaxLongitude = 180;
}
