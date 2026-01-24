using Civica.Api.Data;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Comments;
using Civica.Api.Models.Responses.Comments;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Civica.Api.Services;

public class CommentService(
    ILogger<CommentService> logger,
    CivicaDbContext context,
    IGamificationService gamificationService,
    IActivityService activityService) : ICommentService
{
    private const int PointsForComment = 5;
    private const int PointsForHelpfulVote = 2;

    public async Task<PagedResult<CommentResponse>?> GetIssueCommentsAsync(
        Guid issueId,
        GetCommentsRequest request,
        Guid? currentUserId)
    {
        try
        {
            // Verify issue exists
            var issueExists = await context.Issues.AnyAsync(i => i.Id == issueId);
            if (!issueExists)
            {
                return null;
            }

            var query = context.Comments
                .Include(c => c.User)
                .Where(c => c.IssueId == issueId && !c.IsDeleted);

            // Apply sorting
            query = request.SortBy.ToLowerInvariant() switch
            {
                "helpful" => request.SortDescending
                    ? query.OrderByDescending(c => c.HelpfulCount).ThenByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.HelpfulCount).ThenBy(c => c.CreatedAt),
                _ => request.SortDescending
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt)
            };

            var pageSize = Math.Max(1, request.PageSize);
            var page = Math.Max(1, request.Page);

            var totalCount = await query.CountAsync();

            var comments = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var commentIds = comments.Select(c => c.Id).ToList();

            // Get reply counts for these comments
            var replyCounts = await context.Comments
                .Where(c => c.ParentCommentId != null &&
                           commentIds.Contains(c.ParentCommentId.Value) &&
                           !c.IsDeleted)
                .GroupBy(c => c.ParentCommentId)
                .Select(g => new { ParentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ParentId!.Value, x => x.Count);

            // Get user's votes for these comments if authenticated
            HashSet<Guid> votedCommentIds = [];
            if (currentUserId.HasValue)
            {
                votedCommentIds = (await context.CommentVotes
                    .Where(v => v.UserId == currentUserId.Value && commentIds.Contains(v.CommentId))
                    .Select(v => v.CommentId)
                    .ToListAsync())
                    .ToHashSet();
            }

            return new PagedResult<CommentResponse>
            {
                Items = comments.Select(c => MapToResponse(
                    c,
                    votedCommentIds.Contains(c.Id),
                    replyCounts.GetValueOrDefault(c.Id, 0))).ToList(),
                TotalItems = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting comments for issue: {IssueId}", issueId);
            throw;
        }
    }

    public async Task<CommentResponse?> GetCommentByIdAsync(Guid commentId, Guid? currentUserId)
    {
        try
        {
            var comment = await context.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);

            if (comment == null)
            {
                return null;
            }

            var hasVoted = false;
            if (currentUserId.HasValue)
            {
                hasVoted = await context.CommentVotes
                    .AnyAsync(v => v.CommentId == commentId && v.UserId == currentUserId.Value);
            }

            var replyCount = await context.Comments
                .CountAsync(c => c.ParentCommentId == commentId && !c.IsDeleted);

            return MapToResponse(comment, hasVoted, replyCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting comment: {CommentId}", commentId);
            throw;
        }
    }

    public async Task<CommentResponse> CreateCommentAsync(
        Guid issueId,
        CreateCommentRequest request,
        string supabaseUserId)
    {
        try
        {
            // Get user profile
            var user = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                throw new InvalidOperationException("User not found");
            }

            // Verify issue exists and is active
            var issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                throw new InvalidOperationException("Issue not found");
            }

            if (issue.Status != IssueStatus.Active)
            {
                throw new InvalidOperationException("Cannot comment on non-active issues");
            }

            // Validate parent comment if provided
            if (request.ParentCommentId.HasValue)
            {
                var parentComment = await context.Comments
                    .FirstOrDefaultAsync(c => c.Id == request.ParentCommentId.Value && !c.IsDeleted);

                if (parentComment == null)
                {
                    throw new InvalidOperationException("Parent comment not found");
                }

                if (parentComment.IssueId != issueId)
                {
                    throw new InvalidOperationException("Parent comment belongs to a different issue");
                }
            }

            // Validate content after trimming (handle null from explicit JSON null)
            var trimmedContent = request.Content?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmedContent))
            {
                throw new InvalidOperationException("Comment content cannot be empty or whitespace only");
            }

            // Rate limit: max 1 comment per 10 seconds per user per issue
            var recentComment = await context.Comments
                .Where(c => c.UserId == user.Id
                    && c.IssueId == issueId
                    && !c.IsDeleted
                    && c.CreatedAt > DateTime.UtcNow.AddSeconds(-10))
                .AnyAsync();

            if (recentComment)
            {
                throw new InvalidOperationException("Please wait before posting another comment");
            }

            // Duplicate detection: block identical content within 5 minutes
            var duplicateExists = await context.Comments
                .Where(c => c.UserId == user.Id
                    && c.IssueId == issueId
                    && c.Content == trimmedContent
                    && !c.IsDeleted
                    && c.CreatedAt > DateTime.UtcNow.AddMinutes(-5))
                .AnyAsync();

            if (duplicateExists)
            {
                throw new InvalidOperationException("You have already posted this comment");
            }

            // Create comment
            var comment = new Comment
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                UserId = user.Id,
                Content = trimmedContent,
                ParentCommentId = request.ParentCommentId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Comments.Add(comment);

            // Update user stats
            user.CommentsGiven++;
            user.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            // Award points (separate transaction)
            await gamificationService.AwardPointsAsync(user.Id, PointsForComment, "comment_created");

            // Record activity
            await activityService.RecordActivityAsync(
                ActivityType.NewComment,
                issueId,
                user.Id);

            logger.LogInformation(
                "User {UserId} created comment {CommentId} on issue {IssueId}",
                user.Id, comment.Id, issueId);

            // Reload with user for response
            comment.User = user;
            return MapToResponse(comment, false, 0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating comment on issue: {IssueId}", issueId);
            throw;
        }
    }

    public async Task<(bool Success, string? Error)> UpdateCommentAsync(
        Guid commentId,
        UpdateCommentRequest request,
        string supabaseUserId)
    {
        try
        {
            var user = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                return (false, "User not found");
            }

            var comment = await context.Comments
                .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);

            if (comment == null)
            {
                return (false, "Comment not found");
            }

            if (comment.UserId != user.Id)
            {
                return (false, "You can only edit your own comments");
            }

            // Validate content after trimming (handle null from explicit JSON null)
            var trimmedContent = request.Content?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmedContent))
            {
                return (false, "Comment content cannot be empty or whitespace only");
            }

            comment.Content = trimmedContent;
            comment.IsEdited = true;
            comment.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            logger.LogInformation(
                "User {UserId} updated comment {CommentId}",
                user.Id, commentId);

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating comment: {CommentId}", commentId);
            throw;
        }
    }

    public async Task<(bool Success, string? Error)> DeleteCommentAsync(
        Guid commentId,
        string supabaseUserId,
        bool isAdmin)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var user = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                return (false, "User not found");
            }

            var comment = await context.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);

            if (comment == null)
            {
                return (false, "Comment not found");
            }

            // Check authorization: owner or admin
            if (comment.UserId != user.Id && !isAdmin)
            {
                return (false, "You can only delete your own comments");
            }

            // Atomically soft-delete the comment with optimistic concurrency
            // This prevents double stat deduction if concurrent deletes occur
            var rowsAffected = await context.Comments
                .Where(c => c.Id == commentId && !c.IsDeleted)
                .ExecuteUpdateAsync(c => c
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedByUserId, user.Id)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

            // If no rows affected, another request already deleted this comment
            if (rowsAffected == 0)
            {
                logger.LogInformation(
                    "Comment {CommentId} was already deleted by a concurrent request",
                    commentId);
                await transaction.RollbackAsync();
                return (true, null); // Return success - delete is idempotent
            }

            // Deduct comment creation points from the author
            await gamificationService.DeductPointsAsync(
                comment.UserId,
                PointsForComment,
                "comment_deleted");

            // Decrement the user's CommentsGiven stat
            await context.UserProfiles
                .Where(u => u.Id == comment.UserId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.CommentsGiven, x => Math.Max(0, x.CommentsGiven - 1))
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

            logger.LogInformation(
                "Deducted {Points} creation points from user {UserId} due to comment deletion",
                PointsForComment, comment.UserId);

            // Also adjust helpful vote stats if comment had votes
            if (comment.HelpfulCount > 0)
            {
                // Deduct HelpfulComments stat from comment author
                await context.UserProfiles
                    .Where(u => u.Id == comment.UserId)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.HelpfulComments, x => Math.Max(0, x.HelpfulComments - comment.HelpfulCount))
                        .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                // Deduct points for each helpful vote received
                var pointsToDeduct = comment.HelpfulCount * PointsForHelpfulVote;
                await gamificationService.DeductPointsAsync(
                    comment.UserId,
                    pointsToDeduct,
                    "comment_deleted_votes_removed");

                logger.LogInformation(
                    "Deducted {Points} points and {VoteCount} helpful comments from user {UserId} due to comment deletion",
                    pointsToDeduct, comment.HelpfulCount, comment.UserId);
            }

            // Recursively find all descendants (replies, nested replies, etc.)
            var allDescendants = new List<(Guid Id, Guid UserId, int HelpfulCount)>();
            var parentIds = new List<Guid> { commentId };

            while (parentIds.Count > 0)
            {
                var children = await context.Comments
                    .Where(c => parentIds.Contains(c.ParentCommentId!.Value) && !c.IsDeleted)
                    .Select(c => new { c.Id, c.UserId, c.HelpfulCount })
                    .ToListAsync();

                if (children.Count == 0)
                    break;

                allDescendants.AddRange(children.Select(c => (c.Id, c.UserId, c.HelpfulCount)));
                parentIds = children.Select(c => c.Id).ToList();
            }

            if (allDescendants.Count > 0)
            {
                // Group all descendants by user for stat adjustments
                var descendantsByUser = allDescendants
                    .GroupBy(r => r.UserId)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        CommentCount = g.Count(),
                        TotalHelpfulCount = g.Sum(r => r.HelpfulCount)
                    })
                    .ToList();

                foreach (var descendantStat in descendantsByUser)
                {
                    // Deduct creation points for each cascade-deleted comment
                    var creationPointsToDeduct = descendantStat.CommentCount * PointsForComment;
                    await gamificationService.DeductPointsAsync(
                        descendantStat.UserId,
                        creationPointsToDeduct,
                        "reply_cascade_deleted");

                    // Decrement the user's CommentsGiven stat
                    await context.UserProfiles
                        .Where(u => u.Id == descendantStat.UserId)
                        .ExecuteUpdateAsync(u => u
                            .SetProperty(x => x.CommentsGiven, x => Math.Max(0, x.CommentsGiven - descendantStat.CommentCount))
                            .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                    logger.LogInformation(
                        "Deducted {Points} creation points and {CommentCount} CommentsGiven from user {UserId} due to reply cascade deletion",
                        creationPointsToDeduct, descendantStat.CommentCount, descendantStat.UserId);

                    // Also deduct helpful vote stats if any descendants had votes
                    if (descendantStat.TotalHelpfulCount > 0)
                    {
                        // Deduct HelpfulComments stat from descendant author
                        await context.UserProfiles
                            .Where(u => u.Id == descendantStat.UserId)
                            .ExecuteUpdateAsync(u => u
                                .SetProperty(x => x.HelpfulComments, x => Math.Max(0, x.HelpfulComments - descendantStat.TotalHelpfulCount))
                                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                        // Deduct points for each helpful vote received on descendants
                        var votePointsToDeduct = descendantStat.TotalHelpfulCount * PointsForHelpfulVote;
                        await gamificationService.DeductPointsAsync(
                            descendantStat.UserId,
                            votePointsToDeduct,
                            "reply_cascade_deleted_votes_removed");

                        logger.LogInformation(
                            "Deducted {Points} points and {VoteCount} helpful comments from user {UserId} due to reply cascade deletion",
                            votePointsToDeduct, descendantStat.TotalHelpfulCount, descendantStat.UserId);
                    }
                }

                // Soft-delete all descendants (with !IsDeleted check for concurrent safety)
                var descendantIds = allDescendants.Select(d => d.Id).ToList();
                await context.Comments
                    .Where(c => descendantIds.Contains(c.Id) && !c.IsDeleted)
                    .ExecuteUpdateAsync(c => c
                        .SetProperty(x => x.IsDeleted, true)
                        .SetProperty(x => x.DeletedByUserId, user.Id)
                        .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                logger.LogInformation(
                    "Soft-deleted {DescendantCount} descendants (replies and nested replies) along with parent comment {CommentId}",
                    allDescendants.Count, commentId);
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            logger.LogInformation(
                "Comment {CommentId} deleted by user {UserId} (admin: {IsAdmin})",
                commentId, user.Id, isAdmin);

            return (true, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error deleting comment: {CommentId}", commentId);
            throw;
        }
    }

    public async Task<(bool Success, string? Error)> VoteHelpfulAsync(Guid commentId, string supabaseUserId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var user = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                return (false, "User not found");
            }

            var comment = await context.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);

            if (comment == null)
            {
                return (false, "Comment not found");
            }

            // Cannot vote on own comment
            if (comment.UserId == user.Id)
            {
                return (false, "You cannot vote on your own comment");
            }

            // Check if already voted
            var existingVote = await context.CommentVotes
                .FirstOrDefaultAsync(v => v.CommentId == commentId && v.UserId == user.Id);

            if (existingVote != null)
            {
                return (false, "You have already voted on this comment");
            }

            // Create vote
            var vote = new CommentVote
            {
                Id = Guid.NewGuid(),
                CommentId = commentId,
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow
            };

            context.CommentVotes.Add(vote);

            // Save the vote first to let the unique constraint catch duplicates
            await context.SaveChangesAsync();

            // Use atomic database operations to prevent race conditions on helpful counts
            await context.Comments
                .Where(c => c.Id == commentId)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.HelpfulCount, x => x.HelpfulCount + 1));

            await context.UserProfiles
                .Where(u => u.Id == comment.UserId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.HelpfulComments, x => x.HelpfulComments + 1)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

            // Award points to comment author
            await gamificationService.AwardPointsAsync(
                comment.UserId,
                PointsForHelpfulVote,
                "helpful_vote_received");

            await transaction.CommitAsync();

            logger.LogInformation(
                "User {UserId} voted comment {CommentId} as helpful",
                user.Id, commentId);

            return (true, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
                                           ex.InnerException?.Message.Contains("duplicate") == true)
        {
            await transaction.RollbackAsync();
            return (false, "You have already voted on this comment");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error voting on comment: {CommentId}", commentId);
            throw;
        }
    }

    public async Task<(bool Success, string? Error)> RemoveVoteAsync(Guid commentId, string supabaseUserId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var user = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                return (false, "User not found");
            }

            var comment = await context.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);

            if (comment == null)
            {
                return (false, "Comment not found");
            }

            var vote = await context.CommentVotes
                .FirstOrDefaultAsync(v => v.CommentId == commentId && v.UserId == user.Id);

            if (vote == null)
            {
                return (false, "You have not voted on this comment");
            }

            context.CommentVotes.Remove(vote);
            await context.SaveChangesAsync();

            // Use atomic database operations to prevent race conditions on helpful counts
            await context.Comments
                .Where(c => c.Id == commentId)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.HelpfulCount, x => Math.Max(0, x.HelpfulCount - 1)));

            await context.UserProfiles
                .Where(u => u.Id == comment.UserId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.HelpfulComments, x => Math.Max(0, x.HelpfulComments - 1))
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

            // Deduct points using gamification service (handles level recalculation)
            await gamificationService.DeductPointsAsync(
                comment.UserId,
                PointsForHelpfulVote,
                "helpful_vote_removed");

            await transaction.CommitAsync();

            logger.LogInformation(
                "User {UserId} removed vote from comment {CommentId}, deducted {Points} points from author {AuthorId}",
                user.Id, commentId, PointsForHelpfulVote, comment.UserId);

            return (true, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error removing vote from comment: {CommentId}", commentId);
            throw;
        }
    }

    private static CommentResponse MapToResponse(Comment comment, bool hasVoted, int replyCount)
    {
        return new CommentResponse
        {
            Id = comment.Id,
            IssueId = comment.IssueId,
            Content = comment.Content,
            HelpfulCount = comment.HelpfulCount,
            IsEdited = comment.IsEdited,
            IsDeleted = comment.IsDeleted,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt,
            ParentCommentId = comment.ParentCommentId,
            ReplyCount = replyCount,
            User = new CommentUserResponse
            {
                Id = comment.User.Id,
                DisplayName = comment.User.DisplayName,
                PhotoUrl = comment.User.PhotoUrl,
                Level = comment.User.Level
            },
            HasVoted = hasVoted
        };
    }
}
