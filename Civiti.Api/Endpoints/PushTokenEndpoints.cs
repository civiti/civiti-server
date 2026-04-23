using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Application.Requests.Push;
using Civiti.Application.Services;

namespace Civiti.Api.Endpoints;

public static class PushTokenEndpoints
{
    public static void MapPushTokenEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.User.Base)
            .WithTags("Push Notifications")
            .RequireAuthorization();

        // POST /api/user/push-token
        group.MapPost(ApiRoutes.User.PushToken, async (
            RegisterPushTokenRequest request,
            HttpContext context,
            IUserService userService,
            IPushTokenService pushTokenService,
            CancellationToken ct) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var userId = await userService.GetUserIdAsync(supabaseUserId);
                if (userId == null)
                {
                    return Results.NotFound(new { error = "User profile not found" });
                }

                await pushTokenService.RegisterTokenAsync(userId.Value, request.Token, request.Platform, ct);
                return Results.Ok(new { success = true });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("RegisterPushToken")
        .WithSummary("Register a device push token")
        .WithDescription("Associates an Expo push token with the authenticated user. Idempotent — safe to call on every sign-in. If the token is already registered to a different user, it will be reassigned.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // POST /api/user/push-token/deregister
        group.MapPost(ApiRoutes.User.PushTokenDeregister, async (
            DeregisterPushTokenRequest request,
            HttpContext context,
            IUserService userService,
            IPushTokenService pushTokenService,
            CancellationToken ct) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var userId = await userService.GetUserIdAsync(supabaseUserId);
                if (userId == null)
                {
                    return Results.NotFound(new { error = "User profile not found" });
                }

                await pushTokenService.DeregisterTokenAsync(userId.Value, request.Token, ct);
                return Results.Ok(new { success = true });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("DeregisterPushToken")
        .WithSummary("Deregister a device push token")
        .WithDescription("Removes the association between an Expo push token and the authenticated user. Called on sign-out. Idempotent — returns success even if the token was not found.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);
    }
}
