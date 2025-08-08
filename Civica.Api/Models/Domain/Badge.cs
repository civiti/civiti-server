namespace Civica.Api.Models.Domain;

public class Badge
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public BadgeCategory Category { get; set; }
    public BadgeRarity Rarity { get; set; } = BadgeRarity.Common;
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
    Starter,
    Progress,
    Achievement,
    Special
}

public enum BadgeRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}