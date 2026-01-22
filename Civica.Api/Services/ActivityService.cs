using System.Text.Json;
using Civica.Api.Services.Interfaces;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Activity;
using Civica.Api.Models.Responses.Activity;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Civica.Api.Services;

public class ActivityService(
    ILogger<ActivityService> logger,
    CivicaDbContext context)
    : IActivityService
{
    private static readonly TimeSpan SupporterAggregationWindow = TimeSpan.FromHours(1);

    public async Task<PagedResult<ActivityResponse>> GetUserActivitiesAsync(Guid userId, GetActivitiesRequest request)
    {
        try
        {
            var query = context.Activities.Where(a => a.IssueOwnerUserId == userId);
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
            var query = context.Activities
                .Include(a => a.Issue)
                .Where(a => a.Issue.Status == IssueStatus.Active && a.Issue.PublicVisibility);
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

            var activity = new Activity
            {
                Id = Guid.NewGuid(),
                Type = type,
                IssueId = issueId,
                IssueOwnerUserId = issue.UserId,
                ActorUserId = actorUserId,
                IssueTitle = issue.Title,
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

            var windowStart = DateTime.UtcNow.Subtract(SupporterAggregationWindow);

            Activity? existingActivity = await context.Activities
                .Where(a => a.IssueId == issueId
                         && a.Type == ActivityType.NewSupporters
                         && a.CreatedAt >= windowStart)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            if (existingActivity != null)
            {
                existingActivity.AggregatedCount++;
                existingActivity.CreatedAt = DateTime.UtcNow;
                existingActivity.Metadata = SerializeSupporterMetadata(existingActivity.AggregatedCount);
            }
            else
            {
                var activity = new Activity
                {
                    Id = Guid.NewGuid(),
                    Type = ActivityType.NewSupporters,
                    IssueId = issueId,
                    IssueOwnerUserId = issue.UserId,
                    IssueTitle = issue.Title,
                    AggregatedCount = 1,
                    Metadata = SerializeSupporterMetadata(1),
                    CreatedAt = DateTime.UtcNow
                };

                context.Activities.Add(activity);
            }

            await context.SaveChangesAsync();

            logger.LogDebug("Recorded supporter activity for issue {IssueId}", issueId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording supporter activity for issue: {IssueId}", issueId);
            throw;
        }
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

        var totalCount = await query.CountAsync();

        var activities = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResult<ActivityResponse>
        {
            Items = activities.Select(MapToResponse).ToList(),
            TotalItems = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
        };
    }

    private static string SerializeSupporterMetadata(int supporterCount)
    {
        return JsonSerializer.Serialize(new { supporterCount });
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
            _ => $"Activitate pentru \"{activity.IssueTitle}\""
        };
    }
}
