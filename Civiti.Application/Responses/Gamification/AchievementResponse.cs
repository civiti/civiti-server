namespace Civiti.Application.Responses.Gamification;

public class AchievementResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MaxProgress { get; set; }
    public int RewardPoints { get; set; }
    public BadgeResponse? RewardBadge { get; set; }
    public string AchievementType { get; set; } = string.Empty;

    // Romanian localization
    public string TitleRo { get; set; } = string.Empty;
    public string DescriptionRo { get; set; } = string.Empty;
}

public class AchievementProgressResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Progress { get; set; }
    public int MaxProgress { get; set; }
    public int RewardPoints { get; set; }
    public bool Completed { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal PercentageComplete { get; set; }

    // Romanian localization
    public string TitleRo { get; set; } = string.Empty;
    public string DescriptionRo { get; set; } = string.Empty;
}