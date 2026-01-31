namespace Civiti.Api.Models.Responses.Gamification;

public class BadgeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public string? RequirementDescription { get; set; }
    public DateTime? EarnedAt { get; set; }
    public bool IsEarned { get; set; }

    // Romanian localization
    public string NameRo { get; set; } = string.Empty;
    public string DescriptionRo { get; set; } = string.Empty;
    public string CategoryRo { get; set; } = string.Empty;
    public string RarityRo { get; set; } = string.Empty;
    public string? RequirementDescriptionRo { get; set; }
}