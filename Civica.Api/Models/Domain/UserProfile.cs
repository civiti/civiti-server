namespace Civica.Api.Models.Domain;

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
    public bool MonthlyDigestEnabled { get; set; } = false;
    public bool AchievementsEnabled { get; set; } = true;
    public int Points { get; set; } = 0;
    public int Level { get; set; } = 1;
    public int IssuesReported { get; set; } = 0;
    public int IssuesResolved { get; set; } = 0;
    public int CommunityVotes { get; set; } = 0;
    public int CommentsGiven { get; set; } = 0;
    public int HelpfulComments { get; set; } = 0;
    public int VotesGiven { get; set; } = 0;
    public decimal QualityScore { get; set; } = 0;
    public decimal ApprovalRate { get; set; } = 0;
    public int CurrentLoginStreak { get; set; } = 0;
    public int LongestLoginStreak { get; set; } = 0;
    public int CurrentVotingStreak { get; set; } = 0;
    public int LongestVotingStreak { get; set; } = 0;
    public DateTime LastActivityDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool EmailVerified { get; set; } = false;

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