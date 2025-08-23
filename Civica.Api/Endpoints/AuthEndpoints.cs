using Microsoft.AspNetCore.Authorization;
using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Extensions;

namespace Civica.Api.Endpoints;

/// <summary>
/// Authentication and user profile management endpoints
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
            .WithTags("Authentication")
            .WithOpenApi();

        // GET /api/auth/profile
        group.MapGet(ApiRoutes.Auth.Profile, [Authorize] async (
                HttpContext context,
                IAuthService authService) =>
            {
                var userId = context.User.GetUserId();
                UserProfileResponse? profile = await authService.GetUserProfileAsync(userId);

                return profile != null
                    ? Results.Ok(profile)
                    : Results.NotFound(new { error = "User profile not found" });
            })
            .WithName("GetAuthProfile")
            .WithSummary("Get current user profile information")
            .WithDescription(
                "Retrieves the complete profile information for the currently authenticated user, including display name, location, contact details, notification preferences, and gamification data (points and level).")
            .Produces<UserProfileResponse>(200)
            .Produces(401)
            .Produces(404)
            .WithOpenApi();

        // POST /api/auth/profile
        group.MapPost(ApiRoutes.Auth.Profile, [Authorize] async (
                CreateUserProfileRequest request,
                HttpContext context,
                IAuthService authService) =>
            {
                var userId = context.User.GetUserId();
                var email = context.User.GetEmail() ?? throw new UnauthorizedAccessException("Email not found in token");

                try
                {
                    UserProfileResponse profile = await authService.CreateUserProfileAsync(request, userId, email);
                    return Results.Created($"{ApiRoutes.Auth.Base}{ApiRoutes.Auth.Profile}", profile);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("CreateUserProfile")
            .WithSummary("Create user profile after Supabase registration")
            .WithDescription(
                "Creates a new user profile in the Civica system after successful Supabase authentication. This endpoint should be called once per user immediately after their first login. The profile includes personal information, location details, and notification preferences.")
            .Produces<UserProfileResponse>(201)
            .Produces(400)
            .Produces(401)
            .WithOpenApi();

        // PUT /api/auth/profile
        group.MapPut(ApiRoutes.Auth.Profile, [Authorize] async (
                UpdateUserProfileRequest request,
                HttpContext context,
                IAuthService authService) =>
            {
                var userId = context.User.GetUserId();

                try
                {
                    UserProfileResponse profile = await authService.UpdateUserProfileAsync(request, userId);
                    return Results.Ok(profile);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = "User profile not found" });
                }
            })
            .WithName("UpdateAuthProfile")
            .WithSummary("Update user profile information")
            .WithDescription(
                "Updates the authenticated user's profile information including display name, location, contact details, and notification preferences. Only non-null fields in the request will be updated.")
            .Produces<UserProfileResponse>(200)
            .Produces(401)
            .Produces(404)
            .WithOpenApi();
    }
}