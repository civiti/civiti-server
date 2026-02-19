using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;

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
        group.MapGet(ApiRoutes.Auth.Status, [Authorize] (HttpContext context) =>
            {
                var supabaseUserId = context.User.GetSupabaseUserId();
                var email = context.User.GetEmail();

                return Results.Ok(new
                {
                    authenticated = true,
                    supabaseUserId,
                    email
                });
            })
            .WithName("GetAuthStatus")
            .WithSummary("Check authentication status")
            .WithDescription("Verifies that the current JWT token is valid and returns basic user identity information from the token claims. Use /api/user/profile to get the full user profile.")
            .Produces(200)
            .Produces(401);
    }
}
