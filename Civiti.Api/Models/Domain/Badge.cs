namespace Civiti.Api.Models.Domain;

public class Badge
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public BadgeCategory Category { get; set; }
    public BadgeRarity Rarity { get; set; }
    public string? RequirementType { get; set; }
    public int? RequirementValue { get; set; }
    public string? RequirementDescription { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public List<UserBadge> UserBadges { get; set; } = [];
}

public enum BadgeCategory
{
    Unspecified = 0,
    Starter = 1,
    Progress = 2,
    Achievement = 3,
    Special = 4
}

public enum BadgeRarity
{
    Unspecified = 0,
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    Epic = 4,
    Legendary = 5
}