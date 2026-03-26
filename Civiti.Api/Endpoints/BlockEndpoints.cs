using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Exceptions;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Models.Responses.User;
using Civiti.Api.Services.Interfaces;

namespace Civiti.Api.Endpoints;

public static class BlockEndpoints
{
    public static void MapBlockEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.User.Base)
            .WithTags("Block")
            .RequireAuthorization();

        // POST /api/user/blocked/{userId}
        group.MapPost(ApiRoutes.Blocked.ById, async (
            Guid userId,
            HttpContext context,
            IBlockService blockService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var (success, data, error) = await blockService.BlockUserAsync(userId, supabaseUserId);

                if (!success)
                {
                    return error switch
                    {
                        DomainErrors.CannotBlockSelf => Results.BadRequest(new { error }),
                        DomainErrors.TargetUserNotFound => Results.NotFound(new { error }),
                        DomainErrors.AlreadyBlocked => Results.Conflict(new { error }),
                        _ => Results.BadRequest(new { error })
                    };
                }

                return Results.Created($"{ApiRoutes.User.Base}{ApiRoutes.Blocked.Base}", data);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("BlockUser")
        .WithSummary("Block a user")
        .WithDescription("Blocks a user. The blocked user's content will be hidden from the authenticated user.")
        .Produces<BlockUserResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        // DELETE /api/user/blocked/{userId}
        group.MapDelete(ApiRoutes.Blocked.ById, async (
            Guid userId,
            HttpContext context,
            IBlockService blockService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var (success, error) = await blockService.UnblockUserAsync(userId, supabaseUserId);

                if (!success)
                {
                    return error switch
                    {
                        DomainErrors.UserNotBlocked => Results.NotFound(new { error }),
                        _ => Results.BadRequest(new { error })
                    };
                }

                return Results.NoContent();
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("UnblockUser")
        .WithSummary("Unblock a user")
        .WithDescription("Removes a user from the authenticated user's block list.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/user/blocked
        group.MapGet(ApiRoutes.Blocked.Base, async (
            HttpContext context,
            IBlockService blockService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var (success, data, error) = await blockService.GetBlockedUsersAsync(supabaseUserId);

                if (!success)
                {
                    return Results.BadRequest(new { error });
                }

                return Results.Ok(data);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("GetBlockedUsers")
        .WithSummary("Get blocked users list")
        .WithDescription("Returns the list of users blocked by the authenticated user, with display name and photo. Results are capped at 500 items.")
        .Produces<List<BlockedUserResponse>>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
    }
}
