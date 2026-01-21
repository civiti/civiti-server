using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Localization;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Endpoints;

/// <summary>
/// Utility endpoints for categories, health checks, and other common operations
/// </summary>
public static class UtilityEndpoints
{
    /// <summary>
    /// Maps utility-related endpoints to the application
    /// </summary>
    public static void MapUtilityEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.Utility.Base)
            .WithTags("Utility")
            .WithOpenApi();

        // GET /api/categories - Get all issue categories with Romanian labels (public)
        group.MapGet(ApiRoutes.Utility.Categories, () =>
        {
            List<CategoryResponse> categories = CategoryLocalization.GetAll();
            return Results.Ok(categories);
        })
        .WithName("GetCategories")
        .WithSummary("Get all issue categories with Romanian labels")
        .WithDescription("""
            Returns all available issue categories with their English values and Romanian display labels.

            Use the 'value' field when submitting issues via the API.
            Use the 'label' field for displaying categories in the UI.

            This is a public endpoint, no authentication required.
            """)
        .Produces<List<CategoryResponse>>(200);
    }
}
