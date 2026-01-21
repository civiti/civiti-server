using Civica.Api.Models.Domain;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Infrastructure.Localization;

/// <summary>
/// Provides Romanian localization for issue categories
/// </summary>
public static class CategoryLocalization
{
    private static readonly Dictionary<IssueCategory, string> RomanianLabels = new()
    {
        { IssueCategory.Infrastructure, "Infrastructură" },
        { IssueCategory.Environment, "Mediu" },
        { IssueCategory.Transportation, "Transport" },
        { IssueCategory.PublicServices, "Servicii Publice" },
        { IssueCategory.Safety, "Siguranță" },
        { IssueCategory.Other, "Altele" }
    };

    /// <summary>
    /// Gets all categories with their Romanian labels
    /// </summary>
    public static List<CategoryResponse> GetAll() =>
        RomanianLabels.Select(kvp => new CategoryResponse
        {
            Value = kvp.Key.ToString(),
            Label = kvp.Value
        }).ToList();

    /// <summary>
    /// Gets the Romanian label for a specific category
    /// </summary>
    public static string GetLabel(IssueCategory category) =>
        RomanianLabels.GetValueOrDefault(category, category.ToString());
}
