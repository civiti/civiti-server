using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Application.Requests.Comments;
using Civiti.Application.Responses.Auth;
using Civiti.Application.Responses.Comments;
using Civiti.Application.Responses.Common;
using Civiti.Application.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Civiti.Api.Endpoints;

/// <summary>
/// Comment endpoints for issues
/// </summary>
public static class CommentEndpoints
{
    /// <summary>
    /// Maps comment-related endpoints to the application
    /// </summary>
    public static void MapCommentEndpoints(this WebApplication app)
    {
        // Issue comments group
        RouteGroupBuilder issueCommentsGroup = app.MapGroup(ApiRoutes.Comments.IssueComments)
            .WithTags("Comments");

        // Comments group for direct comment operations
        RouteGroupBuilder commentsGroup = app.MapGroup(ApiRoutes.Comments.Base)
            .WithTags("Comments");

        // GET /api/issues/{issueId}/comments - Get paginated comments for an issue
        issueCommentsGroup.MapGet("", async (
            Guid issueId,
            int? page,
            int? pageSize,
            string? sortBy,
            bool? sortDescending,
            HttpContext context,
            ICommentService commentService,
            IUserService userService) =>
        {
            GetCommentsRequest request = new()
            {
                Page = page ?? 1,
                PageSize = Math.Min(pageSize ?? 20, 100),
                SortBy = sortBy ?? "date",
                SortDescending = sortDescending ?? true
            };

            // Get current user ID if authenticated
            Guid? currentUserId = null;
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (!string.IsNullOrEmpty(supabaseUserId))
            {
                UserProfileResponse? profile = await userService.GetUserProfileAsync(supabaseUserId);
                currentUserId = profile?.Id;
            }

            PagedResult<CommentResponse>? result = await commentService.GetIssueCommentsAsync(issueId, request, currentUserId);
            return result == null ? Results.NotFound(new { error = DomainErrors.IssueNotFound }) : Results.Ok(result);
        })
        .WithName("GetIssueComments")
        .WithSummary("Get comments for an issue")
        .WithDescription("Retrieves paginated comments for a specific issue. Supports sorting by date or helpful count.")
        .Produces<PagedResult<CommentResponse>>()
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/issues/{issueId}/comments - Create a comment on an issue
        issueCommentsGroup.MapPost("", async (
            Guid issueId,
            CreateCommentRequest request,
            HttpContext context,
            ICommentService commentService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                CommentResponse comment = await commentService.CreateCommentAsync(issueId, request, supabaseUserId);
                return Results.Created($"/api/comments/{comment.Id}", comment);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message switch
                {
                    DomainErrors.IssueNotFound or DomainErrors.ParentCommentNotFound => Results.NotFound(new { error = ex.Message }),
                    DomainErrors.CommentRateLimited => Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status429TooManyRequests),
                    DomainErrors.DuplicateComment => Results.Conflict(new { error = ex.Message }),
                    _ => Results.BadRequest(new { error = ex.Message })
                };
            }
        })
        .RequireAuthorization()
        .WithName("CreateComment")
        .WithSummary("Create a comment on an issue")
        .WithDescription("Creates a new comment on an active issue. Awards points to the user.")
        .Produces<CommentResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status429TooManyRequests);

        // GET /api/comments/{id} - Get a single comment
        commentsGroup.MapGet(ApiRoutes.Comments.ById, async (
            Guid id,
            HttpContext context,
            ICommentService commentService,
            IUserService userService) =>
        {
            // Get current user ID if authenticated
            Guid? currentUserId = null;
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (!string.IsNullOrEmpty(supabaseUserId))
            {
                UserProfileResponse? profile = await userService.GetUserProfileAsync(supabaseUserId);
                currentUserId = profile?.Id;
            }

            CommentResponse? comment = await commentService.GetCommentByIdAsync(id, currentUserId);
            if (comment == null)
            {
                return Results.NotFound(new { error = DomainErrors.CommentNotFound });
            }

            return Results.Ok(comment);
        })
        .WithName("GetComment")
        .WithSummary("Get a single comment")
        .WithDescription("Retrieves a comment by its ID.")
        .Produces<CommentResponse>()
        .Produces(StatusCodes.Status404NotFound);

        // PUT /api/comments/{id} - Update own comment
        commentsGroup.MapPut(ApiRoutes.Comments.ById, async (
            Guid id,
            UpdateCommentRequest request,
            HttpContext context,
            ICommentService commentService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var (success, error) = await commentService.UpdateCommentAsync(id, request, supabaseUserId);
            if (!success)
            {
                return error switch
                {
                    DomainErrors.CommentNotFound => Results.NotFound(new { error }),
                    DomainErrors.EditOwnCommentsOnly => Results.Forbid(),
                    DomainErrors.AccountDeleted => Results.Problem(
                        detail: DomainErrors.AccountDeleted,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account Deleted"),
                    _ => Results.BadRequest(new { error })
                };
            }

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("UpdateComment")
        .WithSummary("Update own comment")
        .WithDescription("Updates a comment. Only the comment author can update their comment.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // DELETE /api/comments/{id} - Delete comment (owner or admin)
        commentsGroup.MapDelete(ApiRoutes.Comments.ById, async (
            Guid id,
            HttpContext context,
            ICommentService commentService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var isAdmin = context.User.IsAdmin();

            var (success, error) = await commentService.DeleteCommentAsync(id, supabaseUserId, isAdmin);
            if (!success)
            {
                return error switch
                {
                    DomainErrors.CommentNotFound => Results.NotFound(new { error }),
                    DomainErrors.DeleteOwnCommentsOnly => Results.Forbid(),
                    DomainErrors.AccountDeleted => Results.Problem(
                        detail: DomainErrors.AccountDeleted,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account Deleted"),
                    _ => Results.BadRequest(new { error })
                };
            }

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("DeleteComment")
        .WithSummary("Delete a comment")
        .WithDescription("Deletes a comment. The comment author or an admin can delete comments.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/comments/{id}/vote - Vote comment as helpful
        commentsGroup.MapPost(ApiRoutes.Comments.Vote, async (
            Guid id,
            HttpContext context,
            ICommentService commentService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var (success, error) = await commentService.VoteHelpfulAsync(id, supabaseUserId);
            if (!success)
            {
                return error switch
                {
                    DomainErrors.CommentNotFound => Results.NotFound(new { error }),
                    DomainErrors.AccountDeleted => Results.Problem(
                        detail: DomainErrors.AccountDeleted,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account Deleted"),
                    _ => Results.BadRequest(new { error })
                };
            }

            return Results.Ok(new CommentVoteResponse { Message = "Vote recorded" });
        })
        .RequireAuthorization()
        .WithName("VoteCommentHelpful")
        .WithSummary("Vote a comment as helpful")
        .WithDescription("Marks a comment as helpful. Awards points to the comment author.")
        .Produces<CommentVoteResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // DELETE /api/comments/{id}/vote - Remove helpful vote
        commentsGroup.MapDelete(ApiRoutes.Comments.Vote, async (
            Guid id,
            HttpContext context,
            ICommentService commentService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            var (success, error) = await commentService.RemoveVoteAsync(id, supabaseUserId);
            if (!success)
            {
                return error switch
                {
                    DomainErrors.CommentNotFound => Results.NotFound(new { error }),
                    DomainErrors.AccountDeleted => Results.Problem(
                        detail: DomainErrors.AccountDeleted,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account Deleted"),
                    _ => Results.BadRequest(new { error })
                };
            }

            return Results.Ok(new CommentVoteResponse { Message = "Vote removed" });
        })
        .RequireAuthorization()
        .WithName("RemoveCommentVote")
        .WithSummary("Remove helpful vote from a comment")
        .WithDescription("Removes a previously cast helpful vote from a comment.")
        .Produces<CommentVoteResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);
    }
}
