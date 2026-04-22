using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Api.Models.Responses.Authority;
using Civiti.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Civiti.Api.Endpoints;

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
            .WithTags("Authorities");

        // GET /api/authorities - List active authorities with optional filtering (public)
        group.MapGet("/", async (
            IAuthorityService authorityService,
            string? city,
            string? district,
            string? search) =>
        {
            List<AuthorityListResponse> authorities = await authorityService
                .GetActiveAuthoritiesAsync(city, district, search);
            return Results.Ok(authorities);
        })
        .WithName("GetAuthorities")
        .WithSummary("Get list of active authorities with optional filtering")
        .WithDescription("""
            Retrieves active authorities that can be selected when creating an issue.

            Filter options:
            - city: Filter by city name (e.g., 'București', 'Cluj-Napoca')
            - district: Filter by district within city (e.g., 'Sector 1'). When specified with city, returns both district-specific and city-wide authorities.
            - search: Search by authority name (case-insensitive, partial match)

            This is a public endpoint, no authentication required.
            """)
        .Produces<List<AuthorityListResponse>>();

        // GET /api/authorities/{id} - Get authority details (public)
        group.MapGet(ApiRoutes.Authorities.ById, async Task<Results<Ok<AuthorityResponse>, NotFound>> (
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
        .Produces<AuthorityResponse>()
        .Produces(404);
    }
}
