using Civiti.Api.Models.Domain;

namespace Civiti.Api.Models.Requests.Auth;

public class UpdateUserProfileRequest
{
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
    public string? County { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public ResidenceType? ResidenceType { get; set; }
    public bool? IssueUpdatesEnabled { get; set; }
    public bool? CommunityNewsEnabled { get; set; }
    public bool? MonthlyDigestEnabled { get; set; }
    public bool? AchievementsEnabled { get; set; }
}