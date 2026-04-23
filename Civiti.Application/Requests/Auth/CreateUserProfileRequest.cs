using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Entities;

namespace Civiti.Application.Requests.Auth;

/// <summary>
/// Request to create a new user profile after Supabase authentication
/// </summary>
public class CreateUserProfileRequest
{
    /// <summary>
    /// User's display name shown publicly
    /// </summary>
    /// <example>Ion Popescu</example>
    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// URL to the user's profile photo
    /// </summary>
    /// <example>https://storage.civiti.ro/avatars/user-123.jpg</example>
    [Url]
    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Romanian county (județ) of residence
    /// </summary>
    /// <example>București</example>
    [MaxLength(100)]
    public string? County { get; set; }

    /// <summary>
    /// City of residence
    /// </summary>
    /// <example>București</example>
    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// District or sector within the city
    /// </summary>
    /// <example>Sector 1</example>
    [MaxLength(100)]
    public string? District { get; set; }

    /// <summary>
    /// Type of residence (house, apartment, etc.)
    /// </summary>
    public ResidenceType? ResidenceType { get; set; }

    /// <summary>
    /// Enable notifications for issue status updates
    /// </summary>
    public bool? IssueUpdatesEnabled { get; set; }

    /// <summary>
    /// Enable community news notifications
    /// </summary>
    public bool? CommunityNewsEnabled { get; set; }

    /// <summary>
    /// Enable monthly digest emails
    /// </summary>
    public bool? MonthlyDigestEnabled { get; set; }

    /// <summary>
    /// Enable achievement and badge notifications
    /// </summary>
    public bool? AchievementsEnabled { get; set; }

    /// <summary>
    /// Enable push notifications (global toggle)
    /// </summary>
    public bool? PushNotificationsEnabled { get; set; }

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
        AchievementsEnabled = AchievementsEnabled,
        PushNotificationsEnabled = PushNotificationsEnabled
    };
}
