using System.Text.Json;
using System.Text.Json.Serialization;
using Civiti.Infrastructure.Data;
using Civiti.Infrastructure.Services.Issues;
using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Domain.Entities;
using Civiti.Domain.Policies;
using Civiti.Domain.Time;
using Civiti.Application.Mapping;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Authority;
using Civiti.Application.Responses.Common;
using Civiti.Application.Responses.Issues;
using Civiti.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Civiti.Infrastructure.Services;

public class IssueService(
    ILogger<IssueService> logger,
    CivitiDbContext context,
    IGamificationService gamificationService,
    IMemoryCache memoryCache,
    IActivityService activityService,
    INotificationService notificationService,
    IAdminNotifier adminNotifier,
    IContentModerationService contentModerationService)
    : IIssueService
{
    private static readonly TimeSpan EmailCooldownDuration = TimeSpan.FromHours(1);
    private const int PointsForIssueVote = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<PagedResult<IssueListResponse>> GetAllIssuesAsync(GetIssuesRequest request, Guid? currentUserId = null)
    {
        try
        {
            // Only these statuses are allowed for public viewing
            IssueStatus[] allowedPublicStatuses = [IssueStatus.Active, IssueStatus.Resolved];

            IQueryable<Issue> query = context.Issues
                .Include(i => i.Photos)
                .AsQueryable();

            // Apply status filter - default to Active only if not specified
            if (request.Statuses != null && request.Statuses.Count > 0)
            {
                // Filter to only allowed public statuses
                List<IssueStatus> validStatuses = request.Statuses
                    .Where(s => allowedPublicStatuses.Contains(s))
                    .ToList();

                if (validStatuses.Count > 0)
                {
                    query = query.Where(i => validStatuses.Contains(i.Status));
                }
                else
                {
                    // No valid statuses provided, default to Active
                    query = query.Where(i => i.Status == IssueStatus.Active);
                }
            }
            else
            {
                // Default: only Active issues
                query = query.Where(i => i.Status == IssueStatus.Active);
            }

            // Apply filters
            if (request.Category.HasValue)
            {
                query = query.Where(i => i.Category == request.Category.Value);
            }

            if (request.Urgency.HasValue)
            {
                query = query.Where(i => i.Urgency == request.Urgency.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.District))
            {
                // Use ToLower for case-insensitive comparison that works with SQL
                var districtLower = request.District.ToLower();
                query = query.Where(i => i.District != null &&
                    i.District.ToLower().Contains(districtLower));
            }

            if (!string.IsNullOrWhiteSpace(request.Address))
            {
                // Use ToLower for case-insensitive comparison that works with SQL
                var addressLower = request.Address.ToLower();
                query = query.Where(i => i.Address.ToLower().Contains(addressLower));
            }

            // Filter out issues from users blocked by the current viewer
            if (currentUserId.HasValue)
            {
                query = query.Where(i =>
                    !context.BlockedUsers.Any(b =>
                        b.UserId == currentUserId.Value &&
                        b.BlockedUserId == i.UserId));
            }

            // Apply sorting
            query = request.SortBy?.ToLower() switch
            {
                "emails" => request.SortDescending ?
                    query.OrderByDescending(i => i.EmailsSent) :
                    query.OrderBy(i => i.EmailsSent),
                "votes" => request.SortDescending ?
                    query.OrderByDescending(i => i.CommunityVotes) :
                    query.OrderBy(i => i.CommunityVotes),
                "urgency" => request.SortDescending ?
                    query.OrderByDescending(i => i.Urgency) :
                    query.OrderBy(i => i.Urgency),
                _ => request.SortDescending ?
                    query.OrderByDescending(i => i.CreatedAt) :
                    query.OrderBy(i => i.CreatedAt)
            };

            var totalItems = await query.CountAsync();

            // Select issues with UserId to check ownership for HasVoted
            var issueData = await query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(i => new
                {
                    Issue = new IssueListResponse
                    {
                        Id = i.Id,
                        Title = i.Title,
                        Description = i.Description.Length > 200 ?
                            i.Description.Substring(0, 197) + "..." : i.Description,
                        Category = i.Category,
                        Address = i.Address,
                        Urgency = i.Urgency,
                        EmailsSent = i.EmailsSent,
                        CommunityVotes = i.CommunityVotes,
                        CreatedAt = i.CreatedAt,
                        UpdatedAt = i.UpdatedAt,
                        MainPhotoUrl = i.Photos
                            .Where(p => p.IsPrimary || i.Photos.Count == 1)
                            .OrderBy(p => p.CreatedAt)
                            .Select(p => p.Url)
                            .FirstOrDefault(),
                        Latitude = i.Latitude,
                        Longitude = i.Longitude,
                        District = i.District,
                        Status = i.Status
                    },
                    i.UserId
                })
                .ToListAsync();

            List<IssueListResponse> items = issueData.Select(d => d.Issue).ToList();

            // Get user's votes for these issues if authenticated (excluding owned issues)
            HashSet<Guid> votedIssueIds = [];
            HashSet<Guid> ownedIssueIds = [];
            if (currentUserId.HasValue)
            {
                ownedIssueIds = issueData
                    .Where(d => d.UserId == currentUserId.Value)
                    .Select(d => d.Issue.Id)
                    .ToHashSet();

                List<Guid> issueIds = items.Select(i => i.Id).ToList();
                votedIssueIds = (await context.IssueVotes
                    .Where(v => v.UserId == currentUserId.Value && issueIds.Contains(v.IssueId))
                    .Select(v => v.IssueId)
                    .ToListAsync())
                    .ToHashSet();
            }

            // Set HasVoted for each item (null if unauthenticated or owner - voting not applicable)
            foreach (IssueListResponse item in items)
            {
                if (!currentUserId.HasValue || ownedIssueIds.Contains(item.Id))
                {
                    item.HasVoted = null;
                }
                else
                {
                    item.HasVoted = votedIssueIds.Contains(item.Id);
                }
            }

            return new PagedResult<IssueListResponse>
            {
                Items = items,
                TotalItems = totalItems,
                Page = request.Page,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalItems / request.PageSize)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all issues");
            throw;
        }
    }

    public async Task<IssueDetailResponse?> GetIssueByIdAsync(Guid id, Guid? currentUserId = null)
    {
        try
        {
            // Publicly viewable statuses, OR any status when the caller is the creator.
            //
            // The owner clause is load-bearing, not a convenience: it is what lets an owner
            // open the edit form for an issue that is Rejected or awaiting moderation, and
            // what lets them keep working on an Active issue after an edit has pulled it back
            // into the queue. It must stay this narrow — a non-owner passing a known id must
            // not be able to read the content of a non-public issue.
            IQueryable<Issue> issueQuery = context.Issues
                .Include(i => i.Photos)
                .Include(i => i.User)
                .Include(i => i.IssueAuthorities)
                    .ThenInclude(ia => ia.Authority)
                .Where(i => i.Id == id &&
                    (i.Status == IssueStatus.Active || i.Status == IssueStatus.Resolved ||
                     (currentUserId.HasValue && i.UserId == currentUserId.Value)));

            // Filter out issues from users blocked by the current viewer
            if (currentUserId.HasValue)
            {
                issueQuery = issueQuery.Where(i =>
                    !context.BlockedUsers.Any(b =>
                        b.UserId == currentUserId.Value &&
                        b.BlockedUserId == i.UserId));
            }

            Issue? issue = await issueQuery.FirstOrDefaultAsync();

            if (issue == null)
            {
                logger.LogWarning("Issue {IssueId} not found or not in a publicly viewable status", id);
                return null;
            }

            // Check if current user has voted (null if owner or unauthenticated - voting not applicable)
            bool? hasVoted = null;
            if (currentUserId.HasValue && issue.UserId != currentUserId.Value)
            {
                hasVoted = await context.IssueVotes
                    .AnyAsync(v => v.IssueId == id && v.UserId == currentUserId.Value);
            }

            return IssueResponseMapper.ToDetailResponse(issue, hasVoted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting issue {IssueId}", id);
            throw;
        }
    }

    public async Task<CreateIssueResponse> CreateIssueAsync(CreateIssueRequest request, string supabaseUserId)
    {
        // Pre-transaction validation: moderation (external HTTP) and PhotoUrl scheme/length
        // checks happen before BeginTransactionAsync so we don't hold a pooled DB connection
        // across the OpenAI round-trip (300ms-2s). Under concurrency, holding the connection
        // across that RTT exhausts the pool and causes cascading timeouts unrelated to
        // moderation itself.
        //
        // ContentModerationException is the typed signal that callers (CommentService
        // precedent, MCP write tools) distinguish from the unrelated
        // InvalidOperationException family. See the 2026-05-05 prompt-injection review for
        // the threat model.
        await IssueContentModerator.EnsureAllowedAsync(contentModerationService, request);
        IssuePhotoWriter.ValidateUrls(request.PhotoUrls);

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        (Issue createdIssue, UserProfile issueOwner) = await strategy.ExecuteAsync(async () =>
        {
            // Clear change tracker to ensure fresh data on retry (prevents double-incrementing counters)
            context.ChangeTracker.Clear();

            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
            // Get user profile (single query bypassing global filter to distinguish deleted vs missing)
            UserProfile? userProfile = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (userProfile == null)
                throw new InvalidOperationException(DomainErrors.UserProfileNotFound);
            if (userProfile.IsDeleted)
                throw new AccountDeletedException();

            DateTime now = UtcTimestamp.Now();

            // Create the issue
            Issue issue = new()
            {
                Id = Guid.NewGuid(),
                UserId = userProfile.Id,
                Title = request.Title,
                Description = request.Description,
                Category = request.Category!.Value,
                Address = request.Address,
                Latitude = request.Latitude!.Value,
                Longitude = request.Longitude!.Value,
                District = request.District,
                Urgency = request.Urgency,
                DesiredOutcome = request.DesiredOutcome,
                CommunityImpact = request.CommunityImpact,
                Status = IssueStatus.Submitted,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.Issues.Add(issue);

            context.IssuePhotos.AddRange(
                IssuePhotoWriter.Materialize(issue.Id, request.PhotoUrls, now));

            context.IssueAuthorities.AddRange(
                await IssueAuthorityWriter.MaterializeAsync(context, issue.Id, request.Authorities, now));

            // Update user stats
            userProfile.IssuesReported++;
            userProfile.UpdatedAt = now;

            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            // Record activity (outside transaction to avoid circular dependency issues)
            try
            {
                await activityService.RecordActivityAsync(
                    ActivityType.IssueCreated,
                    issue.Id,
                    userProfile.Id);
            }
            catch (Exception activityEx)
            {
                // Log but don't fail the issue creation if activity recording fails
                logger.LogError(activityEx, "Failed to record IssueCreated activity for issue {IssueId}", issue.Id);
            }

            // Send submission confirmation email
            try
            {
                await notificationService.NotifyIssueSubmittedAsync(issue, userProfile);
            }
            catch (Exception notifyEx)
            {
                logger.LogError(notifyEx, "Failed to send submission notification for issue {IssueId}", issue.Id);
            }

            // Announce new issue to admins (async fanout — never blocks the response).
            try
            {
                await adminNotifier.NotifyNewIssueAsync(issue.Id);
            }
            catch (Exception adminNotifyEx)
            {
                logger.LogError(adminNotifyEx, "Failed to enqueue admin notification for issue {IssueId}", issue.Id);
            }

            logger.LogInformation("Issue {IssueId} created successfully by user {UserId}",
                issue.Id, userProfile.Id);

            return (issue, userProfile!);
        }
            catch (AccountDeletedException)
            {
                await transaction.RollbackAsync();
                throw;
            }
            catch (InvalidOperationException)
            {
                await transaction.RollbackAsync();
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "Error creating issue for user {SupabaseUserId}", supabaseUserId);
                throw;
            }
        });

        // Gamification runs AFTER the issue transaction has committed and is deliberately
        // OUTSIDE strategy.ExecuteAsync: if a gamification call threw while still inside the
        // retried lambda, the execution strategy would re-run the whole lambda and create a
        // DUPLICATE issue. It is also a best-effort side effect — a failure in it (e.g. the
        // mid-enumeration InvalidOperationException this PR also fixed) must never roll back
        // or 400 an issue the user validly created. Running it here, once, after commit, also
        // means its queued notifications are enqueued exactly once instead of being replayed
        // on a transient-error retry of the insert.
        try
        {
            // Award points for creating an issue (10 points for submission)
            await gamificationService.AwardPointsAsync(
                issueOwner.Id,
                10,
                "Reported a new community issue");

            // Update achievement progress for issues_reported
            await gamificationService.UpdateAchievementProgressAsync(
                issueOwner.Id,
                "issues_reported");

            // Update quality_photos achievement if issue has 3+ photos
            // Use incremental progress (not absolute) to avoid race conditions with concurrent issue creations
            var photoCount = request.PhotoUrls?.Count(url => !string.IsNullOrWhiteSpace(url)) ?? 0;
            if (photoCount >= 3)
            {
                await gamificationService.UpdateAchievementProgressAsync(
                    issueOwner.Id,
                    "quality_photos");
            }

            // Check for badge eligibility based on new stats
            await gamificationService.CheckAndAwardBadgesAsync(issueOwner.Id);

            // No transaction is open here, so each call above already flushes inline; this
            // is a defensive flush for any still-queued notification.
            await gamificationService.FlushPendingNotificationsAsync();
        }
        catch (Exception gamificationEx)
        {
            logger.LogError(gamificationEx,
                "Gamification failed for issue {IssueId}; the issue was still created successfully",
                createdIssue.Id);
        }

        return new CreateIssueResponse
        {
            Id = createdIssue.Id,
            Status = createdIssue.Status.ToString(),
            CreatedAt = createdIssue.CreatedAt
        };
    }

    public async Task<(bool Success, string? Error)> IncrementEmailCountAsync(Guid issueId, string? clientIp)
    {
        try
        {
            // Check rate limiting (1 hour cooldown per IP per issue)
            string cacheKey = $"email-cooldown:{issueId}:{clientIp ?? "unknown"}";

            if (memoryCache.TryGetValue(cacheKey, out _))
            {
                logger.LogInformation("Rate limit hit for issue {IssueId} from IP {ClientIp}", issueId, clientIp);
                return (false, IIssueService.RateLimitedError);
            }

            // Check if issue exists and is valid for incrementing
            IssueStatus? issueStatus = await context.Issues
                .Where(i => i.Id == issueId)
                .Select(i => (IssueStatus?)i.Status)
                .FirstOrDefaultAsync();

            if (issueStatus == null)
            {
                logger.LogWarning("Issue {IssueId} not found", issueId);
                return (false, DomainErrors.IssueNotFound);
            }

            // Only allow incrementing for active issues
            if (issueStatus != IssueStatus.Active)
            {
                logger.LogWarning("Attempt to increment email count for non-active issue {IssueId}", issueId);
                return (false, "Issue is not active");
            }

            // Atomic increment with status check to prevent TOCTOU race condition
            // Note: UpdatedAt is intentionally NOT updated - engagement metrics (emails, votes)
            // are not content changes; UpdatedAt reflects when issue content was last modified
            int rowsAffected = await context.Issues
                .Where(i => i.Id == issueId
                         && i.Status == IssueStatus.Active)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(i => i.EmailsSent, i => i.EmailsSent + 1));

            if (rowsAffected == 0)
            {
                // Issue may have been deactivated between check and update
                logger.LogWarning("Failed to increment email count for issue {IssueId} - may have been modified", issueId);
                return (false, "Issue is no longer active");
            }

            // Set cooldown in cache
            memoryCache.Set(cacheKey, true, EmailCooldownDuration);

            // Record supporter activity (with 1-hour aggregation)
            try
            {
                await activityService.RecordSupporterActivityAsync(issueId);
            }
            catch (Exception activityEx)
            {
                // Log but don't fail the email increment if activity recording fails
                logger.LogError(activityEx, "Failed to record supporter activity for issue {IssueId}", issueId);
            }

            logger.LogInformation("Email count incremented for issue {IssueId}", issueId);

            // Check for email support milestone notification
            try
            {
                var issueData = await context.Issues
                    .Where(i => i.Id == issueId)
                    .Select(i => new { i.EmailsSent, i.UserId, i.Title })
                    .FirstOrDefaultAsync();

                if (issueData != null)
                {
                    UserProfile? issueAuthor = await context.UserProfiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == issueData.UserId);

                    if (issueAuthor != null)
                    {
                        Issue milestoneIssue = new() { Id = issueId, Title = issueData.Title };
                        await notificationService.NotifyEmailSupportMilestoneAsync(milestoneIssue, issueAuthor, issueData.EmailsSent);
                    }
                }
            }
            catch (Exception notifyEx)
            {
                logger.LogError(notifyEx, "Failed to check email support milestone for issue {IssueId}", issueId);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error incrementing email count for issue {IssueId}", issueId);
            throw;
        }
    }

    public async Task<PagedResult<IssueListResponse>> GetUserIssuesAsync(string supabaseUserId, GetUserIssuesRequest request)
    {
        try
        {
            // Get user profile (single query bypassing global filter to distinguish deleted vs missing)
            UserProfile? userProfile = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (userProfile == null)
            {
                logger.LogWarning("User profile not found for Supabase user: {SupabaseUserId}", supabaseUserId);
                throw new InvalidOperationException(DomainErrors.UserProfileNotFound);
            }

            if (userProfile.IsDeleted)
            {
                throw new AccountDeletedException();
            }

            IQueryable<Issue> query = context.Issues
                .Include(i => i.Photos)
                .Where(i => i.UserId == userProfile.Id)
                .AsQueryable();

            // Apply status filter if provided
            if (request.Status.HasValue)
            {
                query = query.Where(i => i.Status == request.Status.Value);
            }

            // Apply sorting
            query = request.SortBy?.ToLower() switch
            {
                "status" => request.SortDescending ?
                    query.OrderByDescending(i => i.Status) :
                    query.OrderBy(i => i.Status),
                "emails" => request.SortDescending ?
                    query.OrderByDescending(i => i.EmailsSent) :
                    query.OrderBy(i => i.EmailsSent),
                _ => request.SortDescending ?
                    query.OrderByDescending(i => i.CreatedAt) :
                    query.OrderBy(i => i.CreatedAt)
            };

            var totalItems = await query.CountAsync();

            List<IssueListResponse> items = await query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(i => new IssueListResponse
                {
                    Id = i.Id,
                    Title = i.Title,
                    Description = i.Description.Length > 200 ?
                        i.Description.Substring(0, 197) + "..." : i.Description,
                    Category = i.Category,
                    Address = i.Address,
                    Urgency = i.Urgency,
                    EmailsSent = i.EmailsSent,
                    CommunityVotes = i.CommunityVotes,
                    HasVoted = null, // User's own issues - voting not applicable
                    CreatedAt = i.CreatedAt,
                    MainPhotoUrl = i.Photos
                        .Where(p => p.IsPrimary || i.Photos.Count == 1)
                        .OrderBy(p => p.CreatedAt)
                        .Select(p => p.Url)
                        .FirstOrDefault(),
                    Latitude = i.Latitude,
                    Longitude = i.Longitude,
                    District = i.District,
                    Status = i.Status
                })
                .ToListAsync();

            return new PagedResult<IssueListResponse>
            {
                Items = items,
                TotalItems = totalItems,
                Page = request.Page,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalItems / request.PageSize)
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not AccountDeletedException)
        {
            logger.LogError(ex, "Error getting user issues for {SupabaseUserId}", supabaseUserId);
            throw;
        }
    }

    public async Task<(bool Success, string? Error)> UpdateIssueStatusAsync(
        Guid issueId,
        UpdateIssueStatusRequest request,
        string supabaseUserId,
        bool isAdmin = false)
    {
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<(bool Success, string? Error)>(async () =>
        {
            // Clear change tracker to ensure fresh data on retry (prevents skipping gamification on retry)
            context.ChangeTracker.Clear();

            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Get user profile (single query bypassing global filter to distinguish deleted vs missing)
                UserProfile? userProfile = await context.UserProfiles
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

                if (userProfile == null)
                    return (false, DomainErrors.UserProfileNotFound);
                if (userProfile.IsDeleted)
                    return (false, DomainErrors.AccountDeleted);

                // Get the issue
                Issue? issue = await context.Issues
                    .FirstOrDefaultAsync(i => i.Id == issueId);

                if (issue == null)
                {
                    return (false, DomainErrors.IssueNotFound);
                }

                // Check ownership (admins can bypass)
                if (!isAdmin && issue.UserId != userProfile.Id)
                {
                    return (false, DomainErrors.ChangeOwnIssueStatusOnly);
                }

                // Validate the requested status transition
                var validationError = ValidateStatusTransition(issue.Status, request.Status);
                if (validationError != null)
                {
                    return (false, validationError);
                }

                // Update the status
                IssueStatus previousStatus = issue.Status;
                issue.Status = request.Status;
                issue.UpdatedAt = DateTime.UtcNow;

                // If status changed to Resolved, update gamification for the issue OWNER (not the caller)
                if (request.Status == IssueStatus.Resolved && previousStatus != IssueStatus.Resolved)
                {
                    // Get the issue owner's profile to update their stats
                    UserProfile? issueOwner = issue.UserId == userProfile.Id
                        ? userProfile
                        : await context.UserProfiles.FirstOrDefaultAsync(u => u.Id == issue.UserId);

                    // Owner may be null if their account was soft-deleted (filtered out by
                    // the global query filter). Skip the stat update — the deleted user's
                    // profile is anonymised and no longer participates in gamification.
                    if (issueOwner != null)
                    {
                        issueOwner.IssuesResolved++;
                        issueOwner.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await context.SaveChangesAsync();

                // Award points and check achievements for resolution (to the issue OWNER)
                if (request.Status == IssueStatus.Resolved && previousStatus != IssueStatus.Resolved)
                {
                    // Award 100 points for resolving an issue to the issue owner
                    await gamificationService.AwardPointsAsync(
                        issue.UserId,
                        100,
                        "Issue resolved");

                    // Update achievement progress for issues_resolved
                    await gamificationService.UpdateAchievementProgressAsync(
                        issue.UserId,
                        "issues_resolved");

                    // Check for badge eligibility
                    await gamificationService.CheckAndAwardBadgesAsync(issue.UserId);
                }

                await transaction.CommitAsync();

                // Flush gamification notifications now that the transaction is committed
                await gamificationService.FlushPendingNotificationsAsync();

                // Record activity (outside transaction)
                try
                {
                    ActivityType activityType = request.Status == IssueStatus.Resolved
                        ? ActivityType.IssueResolved
                        : ActivityType.StatusChange;

                    var metadata = JsonSerializer.Serialize(new { previousStatus, newStatus = request.Status }, JsonOptions);

                    await activityService.RecordActivityAsync(
                        activityType,
                        issueId,
                        userProfile.Id,
                        metadata);
                }
                catch (Exception activityEx)
                {
                    logger.LogError(activityEx, "Failed to record status change activity for issue {IssueId}", issueId);
                }

                // Send notifications for status changes
                try
                {
                    if (request.Status == IssueStatus.Resolved)
                    {
                        UserProfile? issueOwner = issue.UserId == userProfile.Id
                            ? userProfile
                            : await context.UserProfiles.AsNoTracking().FirstOrDefaultAsync(u => u.Id == issue.UserId);
                        // Skip notification if owner was soft-deleted (filtered by global query filter)
                        if (issueOwner != null)
                        {
                            await notificationService.NotifyIssueResolvedAsync(issue, issueOwner);
                        }
                    }
                    else if (request.Status == IssueStatus.Cancelled)
                    {
                        await notificationService.NotifyIssueCancelledAsync(issueId);
                    }
                }
                catch (Exception notifyEx)
                {
                    logger.LogError(notifyEx, "Failed to send status change notification for issue {IssueId}", issueId);
                }

                logger.LogInformation("Issue {IssueId} status changed to {NewStatus} by user {UserId}",
                    issueId, request.Status, userProfile.Id);

                return (true, null);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "Error updating status for issue {IssueId}", issueId);
                throw;
            }
        });
    }

    /// <summary>
    /// Validates a status change requested by an issue's own author.
    /// Returns null if allowed, or an error message if not.
    /// </summary>
    private static string? ValidateStatusTransition(IssueStatus currentStatus, IssueStatus newStatus)
    {
        // Users can only set these statuses
        IssueStatus[] allowedUserStatuses = [IssueStatus.Cancelled, IssueStatus.Resolved];

        if (!allowedUserStatuses.Contains(newStatus))
        {
            return $"Users can only set status to: {string.Join(", ", allowedUserStatuses)}";
        }

        // Check if already in the target status
        if (currentStatus == newStatus)
        {
            return $"Issue is already {newStatus}";
        }

        // Cannot transition from terminal states
        if (currentStatus == IssueStatus.Cancelled)
        {
            return "Cannot change status of a cancelled issue";
        }

        if (currentStatus == IssueStatus.Resolved && newStatus != IssueStatus.Resolved)
        {
            return "Cannot change status of a resolved issue";
        }

        // Resolving is only meaningful for an issue that actually went live, and restricting it
        // to that case is load-bearing rather than tidy: Resolved is publicly viewable, so any
        // path into it is a path into publication. Without this, an author could submit an issue
        // and immediately resolve it — publishing arbitrary content that no admin ever saw — or
        // edit an approved issue and resolve it straight back into public view, skipping the
        // re-review the edit was supposed to force.
        if (newStatus == IssueStatus.Resolved && currentStatus != IssueStatus.Active)
        {
            return "Only an issue that is currently live can be marked as resolved";
        }

        return null; // Valid transition
    }

    /// <summary>
    /// Reads the caller and the issue and asks whether this edit may proceed.
    /// Returns <c>null</c> when it may.
    /// <para>
    /// Runs outside any transaction, before the billed moderation call, purely so an
    /// unauthorized caller is turned away first. It is a snapshot and therefore advisory:
    /// <see cref="EvaluateEditPreconditions"/> is re-run inside the transaction, where the
    /// answer is binding.
    /// </para>
    /// </summary>
    private async Task<UpdateIssueResult?> CheckEditPreconditionsAsync(
        Guid issueId,
        string supabaseUserId,
        DateTime expectedUpdatedAt)
    {
        UserProfile? userProfile = await context.UserProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (userProfile == null)
        {
            return UpdateIssueResult.Failed(
                UpdateIssueOutcome.UserProfileNotFound, DomainErrors.UserProfileNotFound);
        }

        if (userProfile.IsDeleted)
        {
            return UpdateIssueResult.Failed(
                UpdateIssueOutcome.AccountDeleted, DomainErrors.AccountDeleted);
        }

        Issue? issue = await context.Issues
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == issueId);

        return EvaluateEditPreconditions(issue, userProfile, expectedUpdatedAt);
    }

    /// <summary>
    /// The edit preconditions themselves, over already-loaded state.
    /// Returns <c>null</c> when the edit may proceed.
    /// </summary>
    private static UpdateIssueResult? EvaluateEditPreconditions(
        Issue? issue,
        UserProfile caller,
        DateTime expectedUpdatedAt)
    {
        if (issue == null)
        {
            return UpdateIssueResult.Failed(
                UpdateIssueOutcome.IssueNotFound, DomainErrors.IssueNotFound);
        }

        if (issue.UserId != caller.Id)
        {
            return UpdateIssueResult.Failed(
                UpdateIssueOutcome.NotOwner, DomainErrors.EditOwnIssuesOnly);
        }

        if (!IssueEditPolicy.IsEditable(issue.Status))
        {
            return UpdateIssueResult.Failed(
                UpdateIssueOutcome.StatusNotEditable,
                $"An issue with status '{issue.Status}' can no longer be edited.");
        }

        // Both sides are truncated to the microsecond the database can actually store.
        // Truncating only the incoming token would break every row written before this service
        // started stamping truncated timestamps: those carry sub-microsecond .NET ticks, the
        // client faithfully echoes them back, and the comparison could never succeed — locking
        // the owner out of their own issue for good.
        if (UtcTimestamp.NormalizeToUtc(expectedUpdatedAt) != UtcTimestamp.Truncate(issue.UpdatedAt))
        {
            return UpdateIssueResult.Failed(
                UpdateIssueOutcome.ConcurrencyConflict, DomainErrors.IssueEditConflict);
        }

        return null;
    }

    public async Task<UpdateIssueResult> UpdateIssueAsync(
        Guid issueId,
        UpdateIssueRequest request,
        string supabaseUserId)
    {
        // Authorization first. Moderation is an external, billed call, so it must not be
        // reachable by a caller who has no right to edit this issue in the first place —
        // otherwise any authenticated user can burn the moderation budget (and probe for
        // moderation-specific responses) against issue ids they do not own or that do not
        // exist. This pre-flight is advisory: every check it makes is repeated inside the
        // transaction below, where it is actually enforced.
        UpdateIssueResult? rejection = await CheckEditPreconditionsAsync(
            issueId, supabaseUserId, request.ExpectedUpdatedAt!.Value);

        if (rejection != null)
        {
            return rejection;
        }

        // The same gate CreateIssueAsync applies, and it has to run here too: an edit reaches
        // every read surface a create does, so moderating only on create would be no gate at
        // all — publish something benign, then edit the abusive content in.
        //
        // Still outside any transaction, so the provider round-trip (300ms-2s) is not made
        // while holding a pooled database connection.
        await IssueContentModerator.EnsureAllowedAsync(contentModerationService, request);

        try
        {
            IssuePhotoWriter.ValidateUrls(request.PhotoUrls);
        }
        catch (IssueContentValidationException ex)
        {
            // Reported through the result type like every other caller mistake on this path —
            // letting it escape from outside the transaction below would surface a 500.
            return UpdateIssueResult.Failed(UpdateIssueOutcome.ValidationFailed, ex.Message);
        }

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<UpdateIssueResult>(async () =>
        {
            // Fresh state on retry: this method mutates tracked entities, and replaying it over
            // a dirty change tracker would reapply those edits to stale copies.
            context.ChangeTracker.Clear();

            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Single query bypassing the global filter, so a deleted account stays
                // distinguishable from a missing one.
                UserProfile? userProfile = await context.UserProfiles
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

                if (userProfile == null)
                {
                    return UpdateIssueResult.Failed(
                        UpdateIssueOutcome.UserProfileNotFound, DomainErrors.UserProfileNotFound);
                }

                if (userProfile.IsDeleted)
                {
                    return UpdateIssueResult.Failed(
                        UpdateIssueOutcome.AccountDeleted, DomainErrors.AccountDeleted);
                }

                // Authority is loaded, not just the link rows: the snapshot captured below
                // records authorities by name and email, and a predefined link carries neither
                // of its own. Without it the baseline would store blanks and every later diff
                // would report the authorities as changed — noise on precisely the field a
                // reviewer most needs to trust, since redirecting an approved petition is the
                // edit worth catching.
                Issue? issue = await context.Issues
                    .Include(i => i.Photos)
                    .Include(i => i.IssueAuthorities)
                        .ThenInclude(ia => ia.Authority)
                    .FirstOrDefaultAsync(i => i.Id == issueId);

                // Re-run every precondition against the state as it is now: the pre-flight above
                // was taken before the moderation round-trip, and an admin may have acted in the
                // meantime. Ownership and status are enforced here and nowhere else that matters
                // — the client's checks are advisory, and the request body never gets a say in
                // who owns an issue or what status it is in.
                UpdateIssueResult? blocked = EvaluateEditPreconditions(
                    issue, userProfile, request.ExpectedUpdatedAt!.Value);

                if (blocked != null)
                {
                    return blocked;
                }

                DateTime now = UtcTimestamp.Now();
                DateTime loadedUpdatedAt = issue!.UpdatedAt;
                IssueStatus previousStatus = issue.Status;

                // An issue that is publicly visible right now is showing content an admin
                // approved, and this is the last moment that is still true. Capturing it here
                // gives a diff baseline to every issue approved before the snapshot table
                // existed, which is why this PR ships no data backfill: the alternative was a
                // migration that rebuilt this JSON in SQL and ran against production the moment
                // it merged. Only fills a gap — an existing snapshot is never overwritten, since
                // an edit is not an approval.
                if (IssueEditPolicy.IsPubliclyViewable(previousStatus))
                {
                    await IssueSnapshotStore.CaptureIfMissingAsync(
                        context, issue, issue.ReviewedAt ?? issue.UpdatedAt);
                }

                // Claim the row with a conditional UPDATE before touching anything else.
                //
                // The comparison above comes from a snapshot: two owner requests can both read
                // the same UpdatedAt, both find it current, and both proceed — at which point
                // the second write silently clobbers the first, which is exactly the lost update
                // the token exists to prevent. A conditional UPDATE closes that window, because
                // the loser blocks on the winner's row lock and then re-evaluates its predicate
                // against the committed value, matching zero rows.
                //
                // Scoped to this operation rather than marking UpdatedAt as an EF concurrency
                // token: that would turn every other writer of an Issue — approve, reject,
                // request-changes, votes, email counters — into a DbUpdateConcurrencyException
                // source none of them handle today.
                var claimedRows = await context.Issues
                    .Where(i => i.Id == issueId && i.UpdatedAt == loadedUpdatedAt)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.UpdatedAt, now));

                if (claimedRows == 0)
                {
                    await transaction.RollbackAsync();
                    return UpdateIssueResult.Failed(
                        UpdateIssueOutcome.ConcurrencyConflict, DomainErrors.IssueEditConflict);
                }

                // Full replacement of the editable content — the request carries every field.
                issue.Title = request.Title;
                issue.Description = request.Description;
                issue.Category = request.Category!.Value;
                issue.Address = request.Address;
                issue.District = request.District;
                issue.Latitude = request.Latitude!.Value;
                issue.Longitude = request.Longitude!.Value;
                issue.Urgency = request.Urgency;
                issue.DesiredOutcome = request.DesiredOutcome;
                issue.CommunityImpact = request.CommunityImpact;

                context.IssuePhotos.RemoveRange(issue.Photos);
                context.IssuePhotos.AddRange(
                    IssuePhotoWriter.Materialize(issue.Id, request.PhotoUrls, now));

                // Delete-and-recreate is safe here: nothing in the system emails these rows. The
                // petition goes out from the citizen's own mail client, and
                // POST /api/issues/{id}/email-sent only increments a counter — so replacing a
                // link cannot re-notify anyone who was already contacted.
                context.IssueAuthorities.RemoveRange(issue.IssueAuthorities);
                context.IssueAuthorities.AddRange(
                    await IssueAuthorityWriter.MaterializeAsync(context, issue.Id, request.Authorities, now));

                issue.Status = IssueEditPolicy.ResolveStatusAfterResubmit(previousStatus);
                issue.UpdatedAt = now;

                // Drop the previous review's verdict: it describes content that no longer exists.
                // Left in place it would show the next reviewer a rejection reason for text they
                // are seeing for the first time, and AdminNotes would still hold the change
                // request the owner has just acted on.
                issue.RejectionReason = null;
                issue.ReviewedAt = null;
                issue.ReviewedBy = null;
                issue.AdminNotes = null;

                // EmailsSent and CommunityVotes are deliberately left alone. They record what the
                // community did, not what the text says; resetting them would punish an owner for
                // fixing a typo.

                // The admin announcement is deduplicated per (issue, admin) so that a retrying
                // dispatcher cannot mail the same admin twice. A resubmit is a new announcement
                // cycle rather than a retry, so those markers have to go — otherwise the issue
                // silently reappears in the queue with no one told about it.
                context.AdminIssueNotifications.RemoveRange(
                    context.AdminIssueNotifications.Where(n => n.IssueId == issue.Id));

                // Owner-initiated, so it carries no admin — but it belongs in the moderation
                // history all the same: it is the answer to "why is this back in my queue?".
                context.AdminActions.Add(new AdminAction
                {
                    Id = Guid.NewGuid(),
                    IssueId = issue.Id,
                    AdminUserId = userProfile.Id,
                    AdminSupabaseId = supabaseUserId,
                    ActionType = AdminActionType.Resubmit,
                    Notes = "Edited by the author and resubmitted for approval",
                    PreviousStatus = previousStatus.ToString(),
                    NewStatus = issue.Status.ToString(),
                    CreatedAt = now
                });

                await context.SaveChangesAsync();

                // Reload relations before committing, so a failure while reading them still
                // rolls the edit back.
                await context.Entry(issue).Reference(i => i.User).LoadAsync();
                await context.Entry(issue).Collection(i => i.Photos).LoadAsync();
                await context.Entry(issue).Collection(i => i.IssueAuthorities).LoadAsync();

                foreach (IssueAuthority issueAuthority in issue.IssueAuthorities.Where(ia => ia.AuthorityId.HasValue))
                {
                    await context.Entry(issueAuthority).Reference(x => x.Authority).LoadAsync();
                }

                // hasVoted is null: this is the owner's own issue, and owners cannot vote.
                IssueDetailResponse response = IssueResponseMapper.ToDetailResponse(issue, hasVoted: null);

                await transaction.CommitAsync();

                logger.LogInformation(
                    "Issue {IssueId} edited by its owner {UserId} and resubmitted: {PreviousStatus} -> {NewStatus}",
                    issueId, userProfile.Id, previousStatus, issue.Status);

                // Announce after commit, best-effort — the same fanout a new issue triggers.
                try
                {
                    await adminNotifier.NotifyNewIssueAsync(issue.Id);
                }
                catch (Exception adminNotifyEx)
                {
                    logger.LogError(adminNotifyEx,
                        "Failed to enqueue admin notification for resubmitted issue {IssueId}", issue.Id);
                }

                return UpdateIssueResult.Ok(response);
            }
            catch (IssueContentValidationException ex)
            {
                // A caller mistake in the photo or authority sets, not a fault: surface the
                // reason rather than a 500.
                await transaction.RollbackAsync();
                return UpdateIssueResult.Failed(UpdateIssueOutcome.ValidationFailed, ex.Message);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "Error updating issue {IssueId}", issueId);
                throw;
            }
        });
    }

    public async Task<(bool Success, string? Error)> VoteForIssueAsync(Guid issueId, string supabaseUserId)
    {
        try
        {
            // Pre-validate outside the transaction (single query bypassing global filter to distinguish deleted vs missing)
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
                return (false, DomainErrors.UserNotFound);
            if (user.IsDeleted)
                return (false, DomainErrors.AccountDeleted);

            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return (false, DomainErrors.IssueNotFound);
            }

            // Can only vote on active issues
            if (issue.Status != IssueStatus.Active)
            {
                return (false, "Can only vote on active issues");
            }

            // Cannot vote on own issue
            if (issue.UserId == user.Id)
            {
                return (false, "You cannot vote on your own issue");
            }

            // Check if already voted (for user-friendly error message)
            var alreadyVoted = await context.IssueVotes
                .AnyAsync(v => v.IssueId == issueId && v.UserId == user.Id);

            if (alreadyVoted)
            {
                return (false, "You have already voted on this issue");
            }

            // Use execution strategy to wrap the transaction
            IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

            // Generate ID before retry block for idempotency - if retry occurs after commit,
            // we can detect our already-created vote by this ID
            Guid voteId = Guid.NewGuid();

            var votedByThisRequest = await strategy.ExecuteAsync(async () =>
            {
                // Clear change tracker to ensure clean state on retry
                context.ChangeTracker.Clear();

                // Re-check IsDeleted inside the retry loop to close the TOCTOU window:
                // a concurrent soft-delete between the pre-validation above and the INSERT
                // would otherwise create an orphaned IssueVotes row.
                bool isDeleted = await context.UserProfiles
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == user.Id && u.IsDeleted)
                    .AnyAsync();
                if (isDeleted)
                    return (voted: false, deleted: true);

                // Check if this vote was already created in a previous retry attempt
                IssueVote? existingVote = await context.IssueVotes
                    .FirstOrDefaultAsync(v => v.Id == voteId);

                if (existingVote != null)
                {
                    // Vote was created on a previous attempt - treat as success
                    return (voted: true, deleted: false);
                }

                await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

                // Create vote
                IssueVote vote = new()
                {
                    Id = voteId,
                    IssueId = issueId,
                    UserId = user.Id,
                    CreatedAt = DateTime.UtcNow
                };

                context.IssueVotes.Add(vote);

                // Save the vote first to let the unique constraint catch duplicates
                await context.SaveChangesAsync();

                // Atomic increment with status check to prevent TOCTOU race condition
                // If issue was deactivated between our check and now, this will affect 0 rows
                // Note: UpdatedAt is intentionally NOT updated - engagement metrics (emails, votes)
                // are not content changes; UpdatedAt reflects when issue content was last modified
                int rowsAffected = await context.Issues
                    .Where(i => i.Id == issueId && i.Status == IssueStatus.Active)
                    .ExecuteUpdateAsync(i => i.SetProperty(x => x.CommunityVotes, x => x.CommunityVotes + 1));

                if (rowsAffected == 0)
                {
                    // Issue was deactivated between check and update - rollback everything
                    await transaction.RollbackAsync();
                    return (voted: false, deleted: false);
                }

                // Increment CommunityVotes on the issue author's profile (votes received)
                // Guard against soft-deleted authors explicitly since ExecuteUpdateAsync bypasses global filters
                await context.UserProfiles
                    .Where(u => u.Id == issue.UserId && !u.IsDeleted)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.CommunityVotes, x => x.CommunityVotes + 1)
                        .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                // Increment VotesGiven on the voter's profile
                await context.UserProfiles
                    .Where(u => u.Id == user.Id && !u.IsDeleted)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.VotesGiven, x => x.VotesGiven + 1)
                        .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                // Award points to issue author
                await gamificationService.AwardPointsAsync(
                    issue.UserId,
                    PointsForIssueVote,
                    "issue_vote_received");

                // Check badges for both users
                await gamificationService.CheckAndAwardBadgesAsync(issue.UserId);
                await gamificationService.CheckAndAwardBadgesAsync(user.Id);

                await transaction.CommitAsync();

                // Flush gamification notifications now that the transaction is committed
                await gamificationService.FlushPendingNotificationsAsync();

                return (voted: true, deleted: false);
            });

            if (votedByThisRequest.deleted)
                return (false, DomainErrors.AccountDeleted);

            if (!votedByThisRequest.voted)
            {
                // Issue was deactivated during the voting process
                return (false, "Issue is no longer active");
            }

            logger.LogInformation(
                "User {UserId} voted for issue {IssueId}",
                user.Id, issueId);

            // Check for vote milestone notification
            try
            {
                int updatedVoteCount = await context.Issues
                    .Where(i => i.Id == issueId)
                    .Select(i => i.CommunityVotes)
                    .FirstOrDefaultAsync();

                UserProfile? issueAuthor = await context.UserProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == issue.UserId);

                if (issueAuthor != null)
                {
                    await notificationService.NotifyVoteMilestoneAsync(issue, issueAuthor, updatedVoteCount);
                }
            }
            catch (Exception notifyEx)
            {
                logger.LogError(notifyEx, "Failed to check vote milestone for issue {IssueId}", issueId);
            }

            return (true, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
                                           ex.InnerException?.Message.Contains("duplicate") == true)
        {
            return (false, "You have already voted on this issue");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error voting for issue: {IssueId}", issueId);
            throw;
        }
    }

    public async Task<(bool Success, string? Error)> RemoveVoteAsync(Guid issueId, string supabaseUserId)
    {
        try
        {
            // Pre-validate outside the transaction (single query bypassing global filter to distinguish deleted vs missing)
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
                return (false, DomainErrors.UserNotFound);
            if (user.IsDeleted)
                return (false, DomainErrors.AccountDeleted);

            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return (false, DomainErrors.IssueNotFound);
            }

            // Check if vote exists (for user-friendly error message)
            var voteExists = await context.IssueVotes
                .AnyAsync(v => v.IssueId == issueId && v.UserId == user.Id);

            if (!voteExists)
            {
                return (false, "You have not voted on this issue");
            }

            // Use execution strategy to wrap the transaction
            IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

            var removeResult = await strategy.ExecuteAsync(async () =>
            {
                // Clear change tracker to ensure clean state on retry
                context.ChangeTracker.Clear();

                // Re-check IsDeleted inside the retry loop to close the TOCTOU window
                bool isDeleted = await context.UserProfiles
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == user.Id && u.IsDeleted)
                    .AnyAsync();
                if (isDeleted)
                    return (removed: false, deleted: true);

                await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

                // Use ExecuteDeleteAsync for atomic delete - avoids entity tracking issues on retry
                var rowsDeleted = await context.IssueVotes
                    .Where(v => v.IssueId == issueId && v.UserId == user.Id)
                    .ExecuteDeleteAsync();

                // If no rows deleted, vote was already removed (concurrent request or retry after success)
                if (rowsDeleted == 0)
                {
                    await transaction.RollbackAsync();
                    return (removed: false, deleted: false); // Idempotent success
                }

                // Use atomic database operations to prevent race conditions on vote counts
                // Decrement CommunityVotes on the issue
                // Note: UpdatedAt is intentionally NOT updated - engagement metrics are not content changes
                await context.Issues
                    .Where(i => i.Id == issueId)
                    .ExecuteUpdateAsync(i => i.SetProperty(x => x.CommunityVotes, x => Math.Max(0, x.CommunityVotes - 1)));

                // Decrement CommunityVotes on the issue author's profile (votes received)
                // Guard against soft-deleted authors explicitly since ExecuteUpdateAsync bypasses global filters
                await context.UserProfiles
                    .Where(u => u.Id == issue.UserId && !u.IsDeleted)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.CommunityVotes, x => Math.Max(0, x.CommunityVotes - 1))
                        .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                // Decrement VotesGiven on the voter's profile
                await context.UserProfiles
                    .Where(u => u.Id == user.Id && !u.IsDeleted)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.VotesGiven, x => Math.Max(0, x.VotesGiven - 1))
                        .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));

                // Deduct points using gamification service (handles level recalculation)
                await gamificationService.DeductPointsAsync(
                    issue.UserId,
                    PointsForIssueVote,
                    "issue_vote_removed");

                await transaction.CommitAsync();
                return (removed: true, deleted: false);
            });

            if (removeResult.deleted)
                return (false, DomainErrors.AccountDeleted);

            if (removeResult.removed)
            {
                logger.LogInformation(
                    "User {UserId} removed vote from issue {IssueId}, deducted {Points} points from author {AuthorId}",
                    user.Id, issueId, PointsForIssueVote, issue.UserId);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing vote from issue: {IssueId}", issueId);
            throw;
        }
    }
}
