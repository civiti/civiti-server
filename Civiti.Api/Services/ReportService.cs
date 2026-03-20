using System.Data;
using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Exceptions;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Reports;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Api.Services;

public class ReportService(
    ILogger<ReportService> logger,
    CivitiDbContext context) : IReportService
{
    private const int AutoFlagThreshold = 3;

    public async Task<(bool Success, Guid? ReportId, string? Error)> ReportIssueAsync(
        Guid issueId,
        CreateReportRequest request,
        string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
                return (false, null, DomainErrors.UserNotFound);
            if (user.IsDeleted)
                throw new AccountDeletedException();

            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId && i.Status == IssueStatus.Active);

            if (issue == null)
                return (false, null, DomainErrors.IssueNotFound);

            if (issue.UserId == user.Id)
                return (false, null, DomainErrors.CannotReportOwnContent);

            // Pre-generate ID for idempotency on retry
            Guid reportId = Guid.NewGuid();
            string? businessError = null;

            // Execution strategy wrapper required for retry-enabled DbContext
            var strategy = context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();

                // Idempotency: if report was created in a previous retry attempt, skip
                if (await context.Reports.AnyAsync(r => r.Id == reportId))
                    return;

                // Serializable transaction covers duplicate-check + rate-limit + insert.
                // User/entity lookups above are safe outside it (ownership is immutable).
                await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                // Check for duplicate report (more specific error — check first)
                var alreadyReported = await context.Reports
                    .AnyAsync(r => r.ReporterId == user.Id
                        && r.TargetType == ReportTargetTypes.Issue
                        && r.TargetId == issueId);

                if (alreadyReported)
                {
                    businessError = DomainErrors.AlreadyReported;
                    return;
                }

                // DB-based rate limit: max reports in last hour
                var recentReportCount = await context.Reports
                    .CountAsync(r => r.ReporterId == user.Id
                        && r.CreatedAt > DateTime.UtcNow.AddHours(-1));

                if (recentReportCount >= 5)
                {
                    businessError = DomainErrors.ReportRateLimited;
                    return;
                }

                Report report = new()
                {
                    Id = reportId,
                    ReporterId = user.Id,
                    TargetType = ReportTargetTypes.Issue,
                    TargetId = issueId,
                    Reason = request.ParsedReason,
                    Details = request.Details?.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                context.Reports.Add(report);
                await context.SaveChangesAsync();

                // Atomic increment + auto-flag — prevents lost updates under concurrency.
                // Issues are flagged (not hidden) so admins can review them;
                // admin action is required to actually reject a flagged issue.
                await context.Issues
                    .Where(i => i.Id == issueId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.ReportCount, i => i.ReportCount + 1)
                        .SetProperty(i => i.IsFlagged, i => i.IsFlagged || (i.ReportCount + 1) >= AutoFlagThreshold));

                await tx.CommitAsync();
            });

            if (businessError != null)
                return (false, null, businessError);

            logger.LogInformation(
                "User {UserId} reported issue {IssueId} for {Reason}",
                user.Id, issueId, request.Reason);

            return (true, reportId, null);
        }
        catch (AccountDeletedException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            // Unique constraint violation from concurrent duplicate report
            return (false, null, DomainErrors.AlreadyReported);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reporting issue: {IssueId}", issueId);
            throw;
        }
    }

    public async Task<(bool Success, Guid? ReportId, string? Error)> ReportCommentAsync(
        Guid commentId,
        CreateReportRequest request,
        string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
                return (false, null, DomainErrors.UserNotFound);
            if (user.IsDeleted)
                throw new AccountDeletedException();

            Comment? comment = await context.Comments
                .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted
                    && c.Issue.Status == IssueStatus.Active);

            if (comment == null)
                return (false, null, DomainErrors.CommentNotFound);

            if (comment.UserId == user.Id)
                return (false, null, DomainErrors.CannotReportOwnContent);

            // Pre-generate ID for idempotency on retry
            Guid reportId = Guid.NewGuid();
            string? businessError = null;

            // Execution strategy wrapper required for retry-enabled DbContext
            var strategy = context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();

                // Idempotency: if report was created in a previous retry attempt, skip
                if (await context.Reports.AnyAsync(r => r.Id == reportId))
                    return;

                // Serializable transaction covers duplicate-check + rate-limit + insert.
                // User/entity lookups above are safe outside it (ownership is immutable).
                await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                // Check for duplicate report (more specific error — check first)
                var alreadyReported = await context.Reports
                    .AnyAsync(r => r.ReporterId == user.Id
                        && r.TargetType == ReportTargetTypes.Comment
                        && r.TargetId == commentId);

                if (alreadyReported)
                {
                    businessError = DomainErrors.AlreadyReported;
                    return;
                }

                // DB-based rate limit: max reports in last hour
                var recentReportCount = await context.Reports
                    .CountAsync(r => r.ReporterId == user.Id
                        && r.CreatedAt > DateTime.UtcNow.AddHours(-1));

                if (recentReportCount >= 5)
                {
                    businessError = DomainErrors.ReportRateLimited;
                    return;
                }

                Report report = new()
                {
                    Id = reportId,
                    ReporterId = user.Id,
                    TargetType = ReportTargetTypes.Comment,
                    TargetId = commentId,
                    Reason = request.ParsedReason,
                    Details = request.Details?.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                context.Reports.Add(report);
                await context.SaveChangesAsync();

                // Atomic increment + auto-hide — prevents lost updates under concurrency
                await context.Comments
                    .Where(c => c.Id == commentId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.ReportCount, c => c.ReportCount + 1)
                        .SetProperty(c => c.IsHidden, c => c.IsHidden || (c.ReportCount + 1) >= AutoFlagThreshold));

                await tx.CommitAsync();
            });

            if (businessError != null)
                return (false, null, businessError);

            logger.LogInformation(
                "User {UserId} reported comment {CommentId} for {Reason}",
                user.Id, commentId, request.Reason);

            return (true, reportId, null);
        }
        catch (AccountDeletedException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            // Unique constraint violation from concurrent duplicate report
            return (false, null, DomainErrors.AlreadyReported);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reporting comment: {CommentId}", commentId);
            throw;
        }
    }
}
