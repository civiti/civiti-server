using Civiti.Api.Models.Domain;

namespace Civiti.Api.Infrastructure.Localization;

/// <summary>
/// Provides Romanian localization for badges
/// </summary>
public static class BadgeLocalization
{
    private static readonly Dictionary<BadgeCategory, string> CategoryTranslations = new()
    {
        { BadgeCategory.Starter, "Începător" },
        { BadgeCategory.Progress, "Progres" },
        { BadgeCategory.Achievement, "Realizare" },
        { BadgeCategory.Special, "Special" }
    };

    private static readonly Dictionary<BadgeRarity, string> RarityTranslations = new()
    {
        { BadgeRarity.Common, "Comun" },
        { BadgeRarity.Uncommon, "Neobișnuit" },
        { BadgeRarity.Rare, "Rar" },
        { BadgeRarity.Epic, "Epic" },
        { BadgeRarity.Legendary, "Legendar" }
    };

    private static readonly Dictionary<string, string> NameTranslations = new()
    {
        { "Civic Starter", "Cetățean Începător" },
        { "Active Citizen", "Cetățean Activ" },
        { "Community Champion", "Campion Comunitar" },
        { "Civic Leader", "Lider Civic" },
        { "Picture Perfect", "Fotografie Perfectă" },
        { "Photo Journalist", "Foto-Jurnalist" },
        { "Community Voice", "Vocea Comunității" },
        { "Problem Solver", "Rezolvator de Probleme" },
        { "Resolution Expert", "Expert în Rezolvări" },
        { "Master Resolver", "Maestru Rezolvator" },
        { "Dedicated Citizen", "Cetățean Dedicat" },
        { "Consistency King", "Regele Consecvenței" },
        { "Rising Star", "Stea în Ascensiune" },
        { "Veteran", "Veteran" },
        { "Legend", "Legendă" }
    };

    private static readonly Dictionary<string, string> DescriptionTranslations = new()
    {
        { "Reported your first community issue", "Ai raportat prima ta problemă comunitară" },
        { "Reported 5 community issues", "Ai raportat 5 probleme comunitare" },
        { "Reported 15 community issues", "Ai raportat 15 probleme comunitare" },
        { "Reported 30 community issues - a true leader!", "Ai raportat 30 de probleme - un adevărat lider!" },
        { "Uploaded high-quality photos with your reports", "Ai încărcat fotografii de calitate cu rapoartele tale" },
        { "Documented 10 issues with quality photos", "Ai documentat 10 probleme cu fotografii de calitate" },
        { "Voted on 10 community issues", "Ai votat pentru 10 probleme comunitare" },
        { "Had your first issue resolved", "Prima ta problemă a fost rezolvată" },
        { "Had 5 of your issues resolved", "5 dintre problemele tale au fost rezolvate" },
        { "Had 10 of your issues resolved - making real impact!", "10 probleme rezolvate - impact real!" },
        { "Logged in 7 days in a row", "Te-ai conectat 7 zile consecutiv" },
        { "Logged in 30 days in a row - incredible dedication!", "30 de zile consecutiv - dedicare incredibilă!" },
        { "Reached level 5", "Ai atins nivelul 5" },
        { "Reached level 10 - a seasoned civic advocate", "Nivelul 10 - un avocat civic experimentat" },
        { "Reached level 20 - a true civic legend!", "Nivelul 20 - o adevărată legendă civică!" }
    };

    private static readonly Dictionary<string, string> RequirementDescriptionTranslations = new()
    {
        { "Report your first issue", "Raportează prima ta problemă" },
        { "Report 5 issues", "Raportează 5 probleme" },
        { "Report 15 issues", "Raportează 15 probleme" },
        { "Report 30 issues", "Raportează 30 de probleme" },
        { "Upload 3 issues with quality photos", "Trimite 3 probleme cu fotografii de calitate" },
        { "Upload 10 issues with quality photos", "Trimite 10 probleme cu fotografii de calitate" },
        { "Vote on 10 issues", "Votează pentru 10 probleme" },
        { "1 issue resolved", "1 problemă rezolvată" },
        { "5 issues resolved", "5 probleme rezolvate" },
        { "10 issues resolved", "10 probleme rezolvate" },
        { "7-day login streak", "Serie de 7 zile de conectare" },
        { "30-day login streak", "Serie de 30 de zile de conectare" },
        { "Reach level 5", "Atinge nivelul 5" },
        { "Reach level 10", "Atinge nivelul 10" },
        { "Reach level 20", "Atinge nivelul 20" }
    };

    /// <summary>
    /// Gets the Romanian translation for a badge category
    /// </summary>
    public static string GetCategoryRo(BadgeCategory category) =>
        CategoryTranslations.GetValueOrDefault(category, category.ToString());

    /// <summary>
    /// Gets the Romanian translation for a badge rarity
    /// </summary>
    public static string GetRarityRo(BadgeRarity rarity) =>
        RarityTranslations.GetValueOrDefault(rarity, rarity.ToString());

    /// <summary>
    /// Gets the Romanian translation for a badge name
    /// </summary>
    public static string GetNameRo(string name) =>
        NameTranslations.GetValueOrDefault(name, name);

    /// <summary>
    /// Gets the Romanian translation for a badge description
    /// </summary>
    public static string GetDescriptionRo(string description) =>
        DescriptionTranslations.GetValueOrDefault(description, description);

    /// <summary>
    /// Gets the Romanian translation for a requirement description
    /// </summary>
    public static string? GetRequirementDescriptionRo(string? requirementDescription) =>
        requirementDescription != null
            ? RequirementDescriptionTranslations.GetValueOrDefault(requirementDescription, requirementDescription)
            : null;
}
