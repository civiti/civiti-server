using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Extensions;
using Civica.Api.Models.Requests.Comments;
using Civica.Api.Models.Responses.Comments;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Services.Interfaces;

namespace Civica.Api.Endpoints;

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
        var issueCommentsGroup = app.MapGroup(ApiRoutes.Comments.IssueComments)
            .WithTags("Comments")
            .WithOpenApi();

        // Comments group for direct comment operations
        var commentsGroup = app.MapGroup(ApiRoutes.Comments.Base)
            .WithTags("Comments")
            .WithOpenApi();

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
            var request = new GetCommentsRequest
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
                var profile = await userService.GetUserProfileAsync(supabaseUserId);
                currentUserId = profile?.Id;
            }

            var result = await commentService.GetIssueCommentsAsync(issueId, request, currentUserId);
            if (result == null)
            {
                return Results.NotFound(new { error = "Issue not found" });
            }

            return Results.Ok(result);
        })
        .WithName("GetIssueComments")
        .WithSummary("Get comments for an issue")
        .WithDescription("Retrieves paginated comments for a specific issue. Supports sorting by date or helpful count.")
        .Produces<PagedResult<CommentResponse>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

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
                var comment = await commentService.CreateCommentAsync(issueId, request, supabaseUserId);
                return Results.Created($"/api/comments/{comment.Id}", comment);
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message switch
                {
                    "Issue not found" or "Parent comment not found" => Results.NotFound(new { error = ex.Message }),
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
        .WithOpenApi();

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
                var profile = await userService.GetUserProfileAsync(supabaseUserId);
                currentUserId = profile?.Id;
            }

            var comment = await commentService.GetCommentByIdAsync(id, currentUserId);
            if (comment == null)
            {
                return Results.NotFound(new { error = "Comment not found" });
            }

            return Results.Ok(comment);
        })
        .WithName("GetComment")
        .WithSummary("Get a single comment")
        .WithDescription("Retrieves a comment by its ID.")
        .Produces<CommentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

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
                    "Comment not found" => Results.NotFound(new { error }),
                    "You can only edit your own comments" => Results.Forbid(),
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
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

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
                return error == "Comment not found"
                    ? Results.NotFound(new { error })
                    : Results.Forbid();
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
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

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
                return error == "Comment not found"
                    ? Results.NotFound(new { error })
                    : Results.BadRequest(new { error });
            }

            return Results.Ok(new { message = "Vote recorded" });
        })
        .RequireAuthorization()
        .WithName("VoteCommentHelpful")
        .WithSummary("Vote a comment as helpful")
        .WithDescription("Marks a comment as helpful. Awards points to the comment author.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

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
                return error == "Comment not found"
                    ? Results.NotFound(new { error })
                    : Results.BadRequest(new { error });
            }

            return Results.Ok(new { message = "Vote removed" });
        })
        .RequireAuthorization()
        .WithName("RemoveCommentVote")
        .WithSummary("Remove helpful vote from a comment")
        .WithDescription("Removes a previously cast helpful vote from a comment.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();
    }
}
