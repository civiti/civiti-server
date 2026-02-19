using Civiti.Api.Models.Domain;

namespace Civiti.Tests.Helpers;

/// <summary>
/// Fluent builders for domain entities with sensible defaults.
/// Reduces boilerplate when arranging test data.
/// </summary>
public static class TestDataBuilder
{
    public static UserProfile CreateUser(
        Guid? id = null,
        string? supabaseUserId = null,
        string? email = null,
        string? displayName = null,
        int points = 0,
        int level = 1)
    {
        var userId = id ?? Guid.NewGuid();
        return new UserProfile
        {
            Id = userId,
            SupabaseUserId = supabaseUserId ?? $"supabase_{userId:N}",
            Email = email ?? $"user_{userId:N}@test.com",
            DisplayName = displayName ?? $"Test User {userId.ToString()[..8]}",
            County = "București",
            City = "București",
            District = "Sector 1",
            Points = points,
            Level = level,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastActivityDate = DateTime.UtcNow,
            CurrentLoginStreak = 1,
            LongestLoginStreak = 1,
            EmailVerified = true
        };
    }

    public static Issue CreateIssue(
        Guid? id = null,
        Guid? userId = null,
        string? title = null,
        IssueStatus status = IssueStatus.Active,
        IssueCategory category = IssueCategory.Infrastructure,
        UrgencyLevel urgency = UrgencyLevel.Medium,
        int emailsSent = 0,
        int communityVotes = 0)
    {
        return new Issue
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            Title = title ?? "Test Issue",
            Description = "Test issue description with enough content for testing",
            Category = category,
            Address = "Strada Test, Nr. 1",
            District = "Sector 1",
            Latitude = 44.4268,
            Longitude = 26.1025,
            Urgency = urgency,
            Status = status,
            EmailsSent = emailsSent,
            CommunityVotes = communityVotes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Comment CreateComment(
        Guid? id = null,
        Guid? issueId = null,
        Guid? userId = null,
        string? content = null,
        Guid? parentCommentId = null,
        int helpfulCount = 0,
        bool isDeleted = false)
    {
        return new Comment
        {
            Id = id ?? Guid.NewGuid(),
            IssueId = issueId ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            Content = content ?? "Test comment content",
            ParentCommentId = parentCommentId,
            HelpfulCount = helpfulCount,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Authority CreateAuthority(
        Guid? id = null,
        string? name = null,
        string? email = null,
        bool isActive = true)
    {
        var authorityId = id ?? Guid.NewGuid();
        return new Authority
        {
            Id = authorityId,
            Name = name ?? $"Authority {authorityId.ToString()[..8]}",
            Email = email ?? $"authority_{authorityId:N}@gov.ro",
            County = "București",
            City = "București",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Achievement CreateAchievement(
        Guid? id = null,
        string? title = null,
        string achievementType = "issues_reported",
        int maxProgress = 5,
        int rewardPoints = 100,
        Guid? rewardBadgeId = null)
    {
        return new Achievement
        {
            Id = id ?? Guid.NewGuid(),
            Title = title ?? "Test Achievement",
            Description = "Test achievement description",
            AchievementType = achievementType,
            MaxProgress = maxProgress,
            RewardPoints = rewardPoints,
            RewardBadgeId = rewardBadgeId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Badge CreateBadge(
        Guid? id = null,
        string? name = null,
        BadgeRarity rarity = BadgeRarity.Common,
        string? requirementType = null,
        int? requirementValue = null)
    {
        return new Badge
        {
            Id = id ?? Guid.NewGuid(),
            Name = name ?? "Test Badge",
            Description = "Test badge description",
            Category = BadgeCategory.Progress,
            Rarity = rarity,
            RequirementType = requirementType,
            RequirementValue = requirementValue,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static IssueVote CreateIssueVote(
        Guid? id = null,
        Guid? issueId = null,
        Guid? userId = null)
    {
        return new IssueVote
        {
            Id = id ?? Guid.NewGuid(),
            IssueId = issueId ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static CommentVote CreateCommentVote(
        Guid? id = null,
        Guid? commentId = null,
        Guid? userId = null)
    {
        return new CommentVote
        {
            Id = id ?? Guid.NewGuid(),
            CommentId = commentId ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static AdminAction CreateAdminAction(
        Guid? issueId = null,
        Guid? adminUserId = null,
        AdminActionType actionType = AdminActionType.Approve,
        string? notes = null)
    {
        return new AdminAction
        {
            Id = Guid.NewGuid(),
            IssueId = issueId ?? Guid.NewGuid(),
            AdminUserId = adminUserId,
            ActionType = actionType,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };
    }
}
