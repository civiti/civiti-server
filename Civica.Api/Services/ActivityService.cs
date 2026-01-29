using System.Text.Json;
using Civica.Api.Services.Interfaces;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Activity;
using Civica.Api.Models.Responses.Activity;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Civica.Api.Services;

public class ActivityService(
    ILogger<ActivityService> logger,
    CivicaDbContext context)
    : IActivityService
{
    private static readonly TimeSpan SupporterAggregationWindow = TimeSpan.FromHours(1);
    private const int MaxIssueTitleLength = 500; // Aligned with Issue.Title and ActivityConfiguration
    private const int MaxRetryAttempts = 3;

    // PostgreSQL error code for unique constraint violation
    private const string UniqueViolationSqlState = "23505";

    public async Task<PagedResult<ActivityResponse>> GetUserActivitiesAsync(Guid userId, GetActivitiesRequest request)
    {
        try
        {
            IQueryable<Activity> query = context.Activities.Where(a => a.IssueOwnerUserId == userId);
            return await ExecutePagedQueryAsync(query, request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting user activities for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<PagedResult<ActivityResponse>> GetRecentActivitiesAsync(GetActivitiesRequest request)
    {
        try
        {
            IQueryable<Activity> query = context.Activities
                .Include(a => a.Issue)
                .Where(a => a.Issue.Status == IssueStatus.Active);
            return await ExecutePagedQueryAsync(query, request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting recent activities");
            throw;
        }
    }

    public async Task RecordActivityAsync(ActivityType type, Guid issueId, Guid? actorUserId = null, string? metadata = null)
    {
        try
        {
            Issue? issue = await context.Issues
                .Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                logger.LogWarning("Cannot record activity for non-existent issue: {IssueId}", issueId);
                return;
            }

            UserProfile? actor = null;
            if (actorUserId.HasValue)
            {
                actor = await context.UserProfiles.FindAsync(actorUserId.Value);
            }

            Activity activity = new()
            {
                Id = Guid.NewGuid(),
                Type = type,
                IssueId = issueId,
                IssueOwnerUserId = issue.UserId,
                ActorUserId = actorUserId,
                IssueTitle = TruncateTitle(issue.Title),
                ActorDisplayName = actor?.DisplayName,
                Metadata = metadata,
                AggregatedCount = 1,
                CreatedAt = DateTime.UtcNow
            };

            context.Activities.Add(activity);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Recorded activity {ActivityType} for issue {IssueId}",
                type, issueId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording activity for issue: {IssueId}", issueId);
            throw;
        }
    }

    public async Task RecordSupporterActivityAsync(Guid issueId)
    {
        try
        {
            Issue? issue = await context.Issues.FindAsync(issueId);

            if (issue == null)
            {
                logger.LogWarning("Cannot record supporter activity for non-existent issue: {IssueId}", issueId);
                return;
            }

            DateTime windowStart = DateTime.UtcNow.Subtract(SupporterAggregationWindow);
            DateTime now = DateTime.UtcNow;

            // Use atomic update for count increment to prevent race conditions
            // Metadata is updated separately (eventual consistency is acceptable for activity feed)
            var updatedCount = await context.Activities
                .Where(a => a.IssueId == issueId
                         && a.Type == ActivityType.NewSupporters
                         && a.CreatedAt >= windowStart)
                .OrderByDescending(a => a.CreatedAt)
                .Take(1)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.AggregatedCount, a => a.AggregatedCount + 1)
                    .SetProperty(a => a.CreatedAt, now));

            if (updatedCount > 0)
            {
                // Update metadata separately - eventual consistency is acceptable
                await UpdateSupporterMetadataAsync(issueId, windowStart);
            }
            else
            {
                // No existing activity - create new one with retry logic for TOCTOU race
                await CreateSupporterActivityWithRetryAsync(issue, windowStart, now);
            }

            logger.LogDebug("Recorded supporter activity for issue {IssueId}", issueId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording supporter activity for issue: {IssueId}", issueId);
            throw;
        }
    }

    private async Task UpdateSupporterMetadataAsync(Guid issueId, DateTime windowStart)
    {
        try
        {
            Activity? activity = await context.Activities
                .Where(a => a.IssueId == issueId
                         && a.Type == ActivityType.NewSupporters
                         && a.CreatedAt >= windowStart)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            if (activity != null)
            {
                activity.Metadata = SerializeSupporterMetadata(activity.AggregatedCount);
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            // Metadata update failure is non-critical - log and continue
            logger.LogWarning(ex, "Failed to update supporter metadata for issue {IssueId}", issueId);
        }
    }

    private async Task CreateSupporterActivityWithRetryAsync(Issue issue, DateTime windowStart, DateTime now)
    {
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            Activity activity = new()
            {
                Id = Guid.NewGuid(),
                Type = ActivityType.NewSupporters,
                IssueId = issue.Id,
                IssueOwnerUserId = issue.UserId,
                IssueTitle = TruncateTitle(issue.Title),
                AggregatedCount = 1,
                Metadata = SerializeSupporterMetadata(1),
                CreatedAt = now
            };

            try
            {
                context.Activities.Add(activity);
                await context.SaveChangesAsync();
                return; // Success
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Another concurrent request created the activity - retry with atomic update
                context.Entry(activity).State = EntityState.Detached;

                var updatedCount = await context.Activities
                    .Where(a => a.IssueId == issue.Id
                             && a.Type == ActivityType.NewSupporters
                             && a.CreatedAt >= windowStart)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(1)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(a => a.AggregatedCount, a => a.AggregatedCount + 1)
                        .SetProperty(a => a.CreatedAt, now));

                if (updatedCount > 0)
                {
                    await UpdateSupporterMetadataAsync(issue.Id, windowStart);
                    return; // Success via update
                }

                logger.LogWarning(
                    "Retry attempt {Attempt}/{MaxAttempts} for supporter activity on issue {IssueId}",
                    attempt, MaxRetryAttempts, issue.Id);
            }
            catch (DbUpdateException ex)
            {
                // Non-constraint violation - detach and rethrow
                context.Entry(activity).State = EntityState.Detached;
                throw new InvalidOperationException(
                    $"Failed to create supporter activity for issue {issue.Id}", ex);
            }
        }

        logger.LogError(
            "Failed to record supporter activity after {MaxAttempts} attempts for issue {IssueId}",
            MaxRetryAttempts, issue.Id);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx &&
               pgEx.SqlState == UniqueViolationSqlState;
    }

    private async Task<PagedResult<ActivityResponse>> ExecutePagedQueryAsync(
        IQueryable<Activity> query,
        GetActivitiesRequest request)
    {
        if (request.Type.HasValue)
        {
            query = query.Where(a => a.Type == request.Type.Value);
        }

        if (request.Since.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= request.Since.Value);
        }

        // Ensure PageSize is at least 1 to prevent division by zero
        var pageSize = Math.Max(1, request.PageSize);
        var page = Math.Max(1, request.Page);

        var totalCount = await query.CountAsync();

        List<Activity> activities = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<ActivityResponse>
        {
            Items = activities.Select(MapToResponse).ToList(),
            TotalItems = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static string SerializeSupporterMetadata(int supporterCount)
    {
        return JsonSerializer.Serialize(new { supporterCount });
    }

    private static string TruncateTitle(string title)
    {
        if (string.IsNullOrEmpty(title) || title.Length <= MaxIssueTitleLength)
            return title;

        return title[..MaxIssueTitleLength];
    }

    private static ActivityResponse MapToResponse(Activity activity)
    {
        return new ActivityResponse
        {
            Id = activity.Id,
            Type = activity.Type,
            IssueId = activity.IssueId,
            IssueTitle = activity.IssueTitle,
            Message = GenerateMessage(activity),
            AggregatedCount = activity.AggregatedCount,
            ActorDisplayName = activity.ActorDisplayName,
            CreatedAt = activity.CreatedAt
        };
    }

    private static string GenerateMessage(Activity activity)
    {
        return activity.Type switch
        {
            ActivityType.NewSupporters when activity.AggregatedCount == 1 =>
                $"Un nou susținător pentru \"{activity.IssueTitle}\"",
            ActivityType.NewSupporters =>
                $"{activity.AggregatedCount} noi susținători pentru \"{activity.IssueTitle}\"",
            ActivityType.StatusChange =>
                $"Statusul problemei \"{activity.IssueTitle}\" a fost actualizat",
            ActivityType.IssueApproved =>
                $"Problema \"{activity.IssueTitle}\" a fost aprobată",
            ActivityType.IssueResolved =>
                $"Problema \"{activity.IssueTitle}\" a fost rezolvată",
            ActivityType.IssueCreated =>
                $"O nouă problemă a fost raportată: \"{activity.IssueTitle}\"",
            ActivityType.NewComment =>
                $"Un nou comentariu la problema \"{activity.IssueTitle}\"",
            _ => $"Activitate pentru \"{activity.IssueTitle}\""
        };
    }
}
