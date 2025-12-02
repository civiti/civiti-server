using Civica.Api.Infrastructure.Constants;
using Civica.Api.Models.Responses.Authority;
using Civica.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Civica.Api.Endpoints;

/// <summary>
/// Authority management endpoints
/// </summary>
public static class AuthorityEndpoints
{
    /// <summary>
    /// Maps authority-related endpoints to the application
    /// </summary>
    public static void MapAuthorityEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.Authorities.Base)
            .WithTags("Authorities")
            .WithOpenApi();

        // GET /api/authorities - List all active predefined authorities (public)
        group.MapGet("/", async (IAuthorityService authorityService) =>
        {
            List<AuthorityListResponse> authorities = await authorityService.GetActiveAuthoritiesAsync();
            return Results.Ok(authorities);
        })
        .WithName("GetAuthorities")
        .WithSummary("Get list of active predefined authorities")
        .WithDescription("Retrieves all active predefined authorities that can be selected when creating an issue. This is a public endpoint, no authentication required.")
        .Produces<List<AuthorityListResponse>>(200);

        // GET /api/authorities/{id} - Get authority details (public)
        group.MapGet("/{id:guid}", async Task<Results<Ok<AuthorityResponse>, NotFound>> (
            IAuthorityService authorityService,
            Guid id) =>
        {
            AuthorityResponse? authority = await authorityService.GetAuthorityByIdAsync(id);

            return authority == null
                ? TypedResults.NotFound()
                : TypedResults.Ok(authority);
        })
        .WithName("GetAuthorityById")
        .WithSummary("Get authority details by ID")
        .WithDescription("Retrieves detailed information about a specific authority.")
        .Produces<AuthorityResponse>(200)
        .Produces(404);
    }
}
