using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Entities;

namespace Civiti.Application.Requests.Auth;

/// <summary>
/// Request to update an existing user profile
/// </summary>
public class UpdateUserProfileRequest
{
    /// <summary>
    /// Updated display name (null to keep current)
    /// </summary>
    /// <example>Ion Popescu</example>
    [MaxLength(100)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Updated profile photo URL (null to keep current)
    /// </summary>
    /// <example>https://storage.civiti.ro/avatars/user-123.jpg</example>
    [Url]
    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Updated county (null to keep current)
    /// </summary>
    [MaxLength(100)]
    public string? County { get; set; }

    /// <summary>
    /// Updated city (null to keep current)
    /// </summary>
    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// Updated district (null to keep current)
    /// </summary>
    [MaxLength(100)]
    public string? District { get; set; }

    /// <summary>
    /// Updated residence type (null to keep current)
    /// </summary>
    public ResidenceType? ResidenceType { get; set; }

    /// <summary>
    /// Update issue update notification preference
    /// </summary>
    public bool? IssueUpdatesEnabled { get; set; }

    /// <summary>
    /// Update community news notification preference
    /// </summary>
    public bool? CommunityNewsEnabled { get; set; }

    /// <summary>
    /// Update monthly digest preference
    /// </summary>
    public bool? MonthlyDigestEnabled { get; set; }

    /// <summary>
    /// Update achievement notification preference
    /// </summary>
    public bool? AchievementsEnabled { get; set; }

    /// <summary>
    /// Update push notification preference (global toggle)
    /// </summary>
    public bool? PushNotificationsEnabled { get; set; }
}
