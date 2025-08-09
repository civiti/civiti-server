using Microsoft.AspNetCore.Authorization;
using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Extensions;

namespace Civica.Api.Endpoints;

public static class AuthEndpoints
{
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
        .Produces<UserProfileResponse>()
        .Produces(404);

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
        .Produces<UserProfileResponse>(201)
        .Produces(400);

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
        .Produces<UserProfileResponse>()
        .Produces(404);
    }
}