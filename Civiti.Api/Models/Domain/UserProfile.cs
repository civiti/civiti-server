namespace Civiti.Api.Models.Domain;

public class UserProfile
{
    public Guid Id { get; set; }
    public string SupabaseUserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? Phone { get; set; }
    public string County { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public ResidenceType? ResidenceType { get; set; }
    public bool IssueUpdatesEnabled { get; set; } = true;
    public bool CommunityNewsEnabled { get; set; } = true;
    public bool MonthlyDigestEnabled { get; set; }
    public bool AchievementsEnabled { get; set; } = true;
    public bool PushNotificationsEnabled { get; set; } = true;
    public int Points { get; set; }
    public int Level { get; set; } = 1;
    public int IssuesReported { get; set; }
    public int IssuesResolved { get; set; }
    public int CommunityVotes { get; set; }
    public int CommentsGiven { get; set; }
    public int HelpfulComments { get; set; }
    public int VotesGiven { get; set; }
    public decimal QualityScore { get; set; }
    public decimal ApprovalRate { get; set; }
    public int CurrentLoginStreak { get; set; }
    public int LongestLoginStreak { get; set; }
    public int CurrentVotingStreak { get; set; }
    public int LongestVotingStreak { get; set; }
    public DateTime LastActivityDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool EmailVerified { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public List<Issue> Issues { get; set; } = [];
    public List<UserBadge> UserBadges { get; set; } = [];
    public List<UserAchievement> UserAchievements { get; set; } = [];
}

public enum ResidenceType
{
    Apartment,
    House,
    Business
}