using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Models.Responses.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Civiti.Api.Endpoints;

/// <summary>
/// Authentication endpoints - token validation and auth status
/// Profile management has been consolidated to UserEndpoints (/api/user/profile)
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps authentication-related endpoints to the application
    /// </summary>
    /// <param name="app">The web application to map endpoints to</param>
    public static void MapAuthEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.Auth.Base)
            .WithTags("Authentication");

        // GET /api/auth/status - Check if user is authenticated and has a valid token
        group.MapGet(ApiRoutes.Auth.Status, [Authorize] Task<Ok<AuthStatusResponse>> (HttpContext context) =>
            {
                var supabaseUserId = context.User.GetSupabaseUserId();
                var email = context.User.GetEmail();

                return Task.FromResult(TypedResults.Ok(new AuthStatusResponse
                {
                    Authenticated = true,
                    SupabaseUserId = supabaseUserId,
                    Email = email
                }));
            })
            .WithName("GetAuthStatus")
            .WithSummary("Check authentication status")
            .WithDescription("Verifies that the current JWT token is valid and returns basic user identity information from the token claims. Use /api/user/profile to get the full user profile.")
            .Produces<AuthStatusResponse>()
            .Produces(401);
    }
}
