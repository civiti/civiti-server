namespace Civiti.Api.Infrastructure.Extensions;

/// <summary>
/// Registration data extracted from Supabase user_metadata.
/// Stored in Supabase's data property during signup, carried in the JWT after email confirmation.
/// Nullable fields distinguish "not set" from explicit values.
/// </summary>
public record SignupMetadata
{
    // Location
    public string? County { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? ResidenceType { get; init; }

    // Notification preferences
    public bool? IssueUpdatesEnabled { get; init; }
    public bool? CommunityNewsEnabled { get; init; }
    public bool? MonthlyDigestEnabled { get; init; }
    public bool? AchievementsEnabled { get; init; }
}