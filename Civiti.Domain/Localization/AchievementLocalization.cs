namespace Civiti.Domain.Localization;

/// <summary>
/// Provides Romanian localization for achievements
/// </summary>
public static class AchievementLocalization
{
    private static readonly Dictionary<string, string> TitleTranslations = new()
    {
        { "First Steps", "Primii Pași" },
        { "Getting Started", "La Început" },
        { "Community Champion", "Campion Comunitar" },
        { "Civic Leadership", "Conducere Civică" },
        { "First Resolution", "Prima Rezolvare" },
        { "Resolution Streak", "Serie de Rezolvări" },
        { "Week Warrior", "Războinic Săptămânal" },
        { "Monthly Dedication", "Dedicare Lunară" },
        { "Photography Basics", "Bazele Fotografiei" },
        { "Level Up!", "Nivel Nou!" }
    };

    private static readonly Dictionary<string, string> DescriptionTranslations = new()
    {
        { "Report your first issue to start making a difference", "Raportează prima problemă pentru a face diferența" },
        { "Report 5 issues and become an active citizen", "Raportează 5 probleme și devino cetățean activ" },
        { "Report 15 issues to become a community champion", "Raportează 15 probleme pentru a deveni campion" },
        { "Report 30 issues and become a civic leader", "Raportează 30 de probleme și devino lider civic" },
        { "Get your first issue resolved", "Prima ta problemă rezolvată" },
        { "Get 5 of your issues resolved", "5 probleme rezolvate" },
        { "Log in 7 days in a row", "Conectează-te 7 zile consecutiv" },
        { "Log in 30 days in a row", "Conectează-te 30 de zile consecutiv" },
        { "Submit 3 issues with quality photos", "Trimite 3 probleme cu fotografii de calitate" },
        { "Reach level 5", "Atinge nivelul 5" }
    };

    /// <summary>
    /// Gets the Romanian translation for an achievement title
    /// </summary>
    public static string GetTitleRo(string title) =>
        TitleTranslations.GetValueOrDefault(title, title);

    /// <summary>
    /// Gets the Romanian translation for an achievement description
    /// </summary>
    public static string GetDescriptionRo(string description) =>
        DescriptionTranslations.GetValueOrDefault(description, description);
}
