using Civiti.Application.Requests.Comments;
using Civiti.Application.Responses.Comments;
using Civiti.Application.Responses.Common;

namespace Civiti.Application.Services;

public interface ICommentService
{
    Task<PagedResult<CommentResponse>?> GetIssueCommentsAsync(Guid issueId, GetCommentsRequest request, Guid? currentUserId);
    Task<CommentResponse?> GetCommentByIdAsync(Guid commentId, Guid? currentUserId);
    Task<CommentResponse> CreateCommentAsync(Guid issueId, CreateCommentRequest request, string supabaseUserId);
    Task<(bool Success, string? Error)> UpdateCommentAsync(Guid commentId, UpdateCommentRequest request, string supabaseUserId);
    Task<(bool Success, string? Error)> DeleteCommentAsync(Guid commentId, string supabaseUserId, bool isAdmin);
    Task<(bool Success, string? Error)> VoteHelpfulAsync(Guid commentId, string supabaseUserId);
    Task<(bool Success, string? Error)> RemoveVoteAsync(Guid commentId, string supabaseUserId);
}
