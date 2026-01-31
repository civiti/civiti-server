using Civiti.Api.Models.Domain;

namespace Civiti.Api.Models.Requests.Auth;

public class CreateUserProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? County { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public ResidenceType? ResidenceType { get; set; }
    public bool? IssueUpdatesEnabled { get; set; }
    public bool? CommunityNewsEnabled { get; set; }
    public bool? MonthlyDigestEnabled { get; set; }
    public bool? AchievementsEnabled { get; set; }

    public UpdateUserProfileRequest ToUpdateRequest() => new()
    {
        DisplayName = DisplayName,
        PhotoUrl = PhotoUrl,
        County = County,
        City = City,
        District = District,
        ResidenceType = ResidenceType,
        IssueUpdatesEnabled = IssueUpdatesEnabled,
        CommunityNewsEnabled = CommunityNewsEnabled,
        MonthlyDigestEnabled = MonthlyDigestEnabled,
        AchievementsEnabled = AchievementsEnabled
    };
}