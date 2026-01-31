namespace Civiti.Api.Models.Responses.Common;

/// <summary>
/// Response model for issue categories with localized labels
/// </summary>
public class CategoryResponse
{
    /// <summary>
    /// English enum value for the category (used in API requests)
    /// </summary>
    /// <example>Infrastructure</example>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Romanian display label for the category
    /// </summary>
    /// <example>Infrastructură</example>
    public string Label { get; set; } = string.Empty;
}
