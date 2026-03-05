using System.Text.Json;
using System.Text.Json.Serialization;
using Civiti.Api.Data;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Issues;
using Civiti.Api.Models.Responses.Authority;
using Civiti.Api.Models.Responses.Common;
using Civiti.Api.Models.Responses.Issues;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Civiti.Api.Services;

public class IssueService(
    ILogger<IssueService> logger,
    CivitiDbContext context,
    IGamificationService gamificationService,
    IMemoryCache memoryCache,
    IActivityService activityService,
    INotificationService notificationService)
    : IIssueService
{
    private static readonly TimeSpan EmailCooldownDuration = TimeSpan.FromHours(1);
    private const int PointsForIssueVote = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Error message returned when rate limited. Used by endpoint to detect 429 response.
    /// </summary>
    public const string RateLimitedError = "RATE_LIMITED";
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
                        MainPhotoUrl = i.Photos
                            .Where(p => p.IsPrimary || i.Photos.Count == 1)
                            .OrderBy(p => p.CreatedAt)
                            .Select(p => p.Url)
                            .FirstOrDefault(),
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
            Issue? issue = await context.Issues
                .Include(i => i.Photos)
                .Include(i => i.User)
                .Include(i => i.IssueAuthorities)
                    .ThenInclude(ia => ia.Authority)
                .Where(i => i.Id == id &&
                    (i.Status == IssueStatus.Active || i.Status == IssueStatus.Resolved ||
                     (currentUserId.HasValue && i.UserId == currentUserId.Value)))
                .FirstOrDefaultAsync();

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

            return new IssueDetailResponse
            {
                Id = issue.Id,
                Title = issue.Title,
                Description = issue.Description,
                Category = issue.Category,
                Address = issue.Address,
                Latitude = issue.Latitude,
                Longitude = issue.Longitude,
                District = issue.District,
                Urgency = issue.Urgency,
                Status = issue.Status,
                EmailsSent = issue.EmailsSent,
                CommunityVotes = issue.CommunityVotes,
                HasVoted = hasVoted,
                DesiredOutcome = issue.DesiredOutcome,
                CommunityImpact = issue.CommunityImpact,
                CreatedAt = issue.CreatedAt,
                UpdatedAt = issue.UpdatedAt,
                Photos = issue.Photos.Select(p => new IssuePhotoResponse
                {
                    Id = p.Id,
                    Url = p.Url,
                    Description = p.Description,
                    IsPrimary = p.IsPrimary,
                    CreatedAt = p.CreatedAt
                }).ToList(),
                Authorities = issue.IssueAuthorities.Select(ia => new IssueAuthorityResponse
                {
                    AuthorityId = ia.AuthorityId,
                    Name = ia.Authority?.Name ?? ia.CustomName ?? string.Empty,
                    Email = ia.Authority?.Email ?? ia.CustomEmail ?? string.Empty,
                    IsPredefined = ia.AuthorityId.HasValue
                }).ToList(),
                User = issue.User is not null
                    ? new UserBasicResponse
                    {
                        Id = issue.User.Id,
                        Name = issue.User.DisplayName,
                        PhotoUrl = issue.User.PhotoUrl
                    }
                    : new UserBasicResponse
                    {
                        Id = Guid.Empty,
                        Name = "Deleted User",
                        PhotoUrl = null
                    }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting issue {IssueId}", id);
            throw;
        }
    }

    public async Task<CreateIssueResponse> CreateIssueAsync(CreateIssueRequest request, string supabaseUserId)
    {
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
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
                throw new InvalidOperationException("User profile not found");
            if (userProfile.IsDeleted)
                throw new InvalidOperationException("This account has been deleted.");

            // Create the issue
            Issue issue = new()
            {
                Id = Guid.NewGuid(),
                UserId = userProfile.Id,
                Title = request.Title,
                Description = request.Description,
                Category = request.Category,
                Address = request.Address,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                District = request.District,
                Urgency = request.Urgency,
                DesiredOutcome = request.DesiredOutcome,
                CommunityImpact = request.CommunityImpact,
                Status = IssueStatus.Submitted,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Issues.Add(issue);

            // Add photos if provided
            if (request.PhotoUrls != null && request.PhotoUrls.Any())
            {
                List<string> validPhotoUrls = request.PhotoUrls
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .ToList();

                List<IssuePhoto> photos = validPhotoUrls.Select((url, index) => new IssuePhoto
                {
                    Id = Guid.NewGuid(),
                    IssueId = issue.Id,
                    Url = url,
                    IsPrimary = index == 0,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                context.IssuePhotos.AddRange(photos);
            }

            // Add authorities if provided
            if (request.Authorities != null && request.Authorities.Any())
            {
                // Filter out null elements from the list
                List<IssueAuthorityInput> authorities = request.Authorities.Where(a => a != null).ToList();

                // Check for duplicate AuthorityIds
                List<Guid> predefinedIds = authorities
                    .Where(a => a.AuthorityId.HasValue)
                    .Select(a => a.AuthorityId!.Value)
                    .ToList();

                if (predefinedIds.Count != predefinedIds.Distinct().Count())
                {
                    throw new InvalidOperationException("Duplicate authority IDs are not allowed");
                }

                // Check for duplicate custom emails (only for custom authorities, not predefined)
                List<string> customEmails = authorities
                    .Where(a => !a.AuthorityId.HasValue && !string.IsNullOrWhiteSpace(a.CustomEmail))
                    .Select(a => a.CustomEmail!.ToLowerInvariant())
                    .ToList();

                if (customEmails.Count != customEmails.Distinct().Count())
                {
                    throw new InvalidOperationException("Duplicate custom authority emails are not allowed");
                }

                foreach (IssueAuthorityInput authorityInput in authorities)
                {
                    // Validate: either AuthorityId OR (CustomName AND CustomEmail) must be provided
                    bool hasPredefined = authorityInput.AuthorityId.HasValue;
                    bool hasCustom = !string.IsNullOrWhiteSpace(authorityInput.CustomName) &&
                                     !string.IsNullOrWhiteSpace(authorityInput.CustomEmail);

                    if (!hasPredefined && !hasCustom)
                    {
                        throw new InvalidOperationException(
                            "Each authority must have either an AuthorityId or both CustomName and CustomEmail");
                    }

                    if (hasPredefined && hasCustom)
                    {
                        throw new InvalidOperationException(
                            "Authority cannot have both AuthorityId and custom fields");
                    }

                    // Validate predefined authority exists and is active
                    if (hasPredefined)
                    {
                        bool authorityExists = await context.Authorities
                            .AnyAsync(a => a.Id == authorityInput.AuthorityId && a.IsActive);

                        if (!authorityExists)
                        {
                            throw new InvalidOperationException(
                                $"Authority with ID {authorityInput.AuthorityId} not found or is inactive");
                        }
                    }

                    IssueAuthority issueAuthority = new()
                    {
                        Id = Guid.NewGuid(),
                        IssueId = issue.Id,
                        AuthorityId = authorityInput.AuthorityId,
                        CustomName = hasPredefined ? null : authorityInput.CustomName,
                        CustomEmail = hasPredefined ? null : authorityInput.CustomEmail,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.IssueAuthorities.Add(issueAuthority);
                }
            }

            // Update user stats
            userProfile.IssuesReported++;
            userProfile.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            // Award points for creating an issue (10 points for submission)
            await gamificationService.AwardPointsAsync(
                userProfile.Id,
                10,
                "Reported a new community issue");

            // Update achievement progress for issues_reported
            await gamificationService.UpdateAchievementProgressAsync(
                userProfile.Id,
                "issues_reported");

            // Update quality_photos achievement if issue has 3+ photos
            // Use incremental progress (not absolute) to avoid race conditions with concurrent issue creations
            var photoCount = request.PhotoUrls?.Count(url => !string.IsNullOrWhiteSpace(url)) ?? 0;
            if (photoCount >= 3)
            {
                await gamificationService.UpdateAchievementProgressAsync(
                    userProfile.Id,
                    "quality_photos");
            }

            // Check for badge eligibility based on new stats
            await gamificationService.CheckAndAwardBadgesAsync(userProfile.Id);

            await transaction.CommitAsync();

            // Flush gamification notifications now that the transaction is committed
            await gamificationService.FlushPendingNotificationsAsync();

            // Record activity (outside transaction to avoid circular dependency issues)
            try
            {
                await activityService.RecordActivityAsync(
                    Models.Domain.ActivityType.IssueCreated,
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

            logger.LogInformation("Issue {IssueId} created successfully by user {UserId}",
                issue.Id, userProfile.Id);

            return new CreateIssueResponse
            {
                Id = issue.Id,
                Status = issue.Status.ToString(),
                CreatedAt = issue.CreatedAt
            };
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
                return (false, RateLimitedError);
            }

            // Check if issue exists and is valid for incrementing
            IssueStatus? issueStatus = await context.Issues
                .Where(i => i.Id == issueId)
                .Select(i => (IssueStatus?)i.Status)
                .FirstOrDefaultAsync();

            if (issueStatus == null)
            {
                logger.LogWarning("Issue {IssueId} not found", issueId);
                return (false, "Issue not found");
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
                throw new InvalidOperationException("User profile not found.");
            }

            if (userProfile.IsDeleted)
            {
                throw new InvalidOperationException("This account has been deleted.");
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
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
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
                    return (false, "User profile not found");
                if (userProfile.IsDeleted)
                    return (false, "This account has been deleted.");

                // Get the issue
                Issue? issue = await context.Issues
                    .FirstOrDefaultAsync(i => i.Id == issueId);

                if (issue == null)
                {
                    return (false, "Issue not found");
                }

                // Check ownership (admins can bypass)
                if (!isAdmin && issue.UserId != userProfile.Id)
                {
                    return (false, "You can only change status of your own issues");
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
                        ? Models.Domain.ActivityType.IssueResolved
                        : Models.Domain.ActivityType.StatusChange;

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
    /// Validates if a status transition is allowed for users.
    /// Returns null if valid, or an error message if invalid.
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

        return null; // Valid transition
    }

    public async Task<(bool Success, IssueDetailResponse? Issue, string? Error)> UpdateIssueAsync(
        Guid issueId,
        UpdateIssueRequest request,
        string supabaseUserId)
    {
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<(bool Success, IssueDetailResponse? Issue, string? Error)>(async () =>
        {
            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Get user profile (single query bypassing global filter to distinguish deleted vs missing)
                UserProfile? userProfile = await context.UserProfiles
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

                if (userProfile == null)
                    return (false, null, "User profile not found");
                if (userProfile.IsDeleted)
                    return (false, null, "This account has been deleted.");

                // Get the issue with related data
                Issue? issue = await context.Issues
                    .Include(i => i.Photos)
                    .Include(i => i.IssueAuthorities)
                    .FirstOrDefaultAsync(i => i.Id == issueId);

                if (issue == null)
                {
                    return (false, null, "Issue not found");
                }

                // Check ownership
                if (issue.UserId != userProfile.Id)
                {
                    return (false, null, "You can only edit your own issues");
                }

                // Check if the issue can be edited (not Cancelled or Resolved)
                if (issue.Status == IssueStatus.Cancelled || issue.Status == IssueStatus.Resolved)
                {
                    return (false, null, $"Cannot edit an issue with status '{issue.Status}'.");
                }

                // Track if related entities were modified (photos/authorities)
                bool hasRelatedChanges = false;

                // Update only provided fields
                if (request.Title != null)
                    issue.Title = request.Title;

                if (request.Description != null)
                    issue.Description = request.Description;

                if (request.Category.HasValue)
                    issue.Category = request.Category.Value;

                if (request.Address != null)
                    issue.Address = request.Address;

                if (request.District != null)
                    issue.District = request.District;

                if (request.Latitude.HasValue)
                    issue.Latitude = request.Latitude.Value;

                if (request.Longitude.HasValue)
                    issue.Longitude = request.Longitude.Value;

                if (request.Urgency.HasValue)
                    issue.Urgency = request.Urgency.Value;

                if (request.DesiredOutcome != null)
                    issue.DesiredOutcome = request.DesiredOutcome;

                if (request.CommunityImpact != null)
                    issue.CommunityImpact = request.CommunityImpact;

                // Handle photo updates (replace all if provided)
                if (request.PhotoUrls != null)
                {
                    context.IssuePhotos.RemoveRange(issue.Photos);

                    List<string> validPhotoUrls = request.PhotoUrls
                        .Where(url => !string.IsNullOrWhiteSpace(url))
                        .ToList();

                    if (validPhotoUrls.Any())
                    {
                        List<IssuePhoto> photos = validPhotoUrls.Select((url, index) => new IssuePhoto
                        {
                            Id = Guid.NewGuid(),
                            IssueId = issue.Id,
                            Url = url,
                            IsPrimary = index == 0,
                            CreatedAt = DateTime.UtcNow
                        }).ToList();

                        context.IssuePhotos.AddRange(photos);
                    }
                    hasRelatedChanges = true;
                }

                // Handle authority updates (replace all if provided)
                if (request.Authorities != null)
                {
                    // Remove existing authorities
                    context.IssueAuthorities.RemoveRange(issue.IssueAuthorities);

                    // Add new authorities
                    if (request.Authorities.Any())
                    {
                        List<IssueAuthorityInput> authorities = request.Authorities.Where(a => a != null).ToList();

                        // Check for duplicate AuthorityIds
                        List<Guid> predefinedIds = authorities
                            .Where(a => a.AuthorityId.HasValue)
                            .Select(a => a.AuthorityId!.Value)
                            .ToList();

                        if (predefinedIds.Count != predefinedIds.Distinct().Count())
                        {
                            await transaction.RollbackAsync();
                            return (false, null, "Duplicate authority IDs are not allowed");
                        }

                        // Check for duplicate custom emails
                        List<string> customEmails = authorities
                            .Where(a => !a.AuthorityId.HasValue && !string.IsNullOrWhiteSpace(a.CustomEmail))
                            .Select(a => a.CustomEmail!.ToLowerInvariant())
                            .ToList();

                        if (customEmails.Count != customEmails.Distinct().Count())
                        {
                            await transaction.RollbackAsync();
                            return (false, null, "Duplicate custom authority emails are not allowed");
                        }

                        foreach (IssueAuthorityInput authorityInput in authorities)
                        {
                            bool hasPredefined = authorityInput.AuthorityId.HasValue;
                            bool hasCustom = !string.IsNullOrWhiteSpace(authorityInput.CustomName) &&
                                             !string.IsNullOrWhiteSpace(authorityInput.CustomEmail);

                            if (!hasPredefined && !hasCustom)
                            {
                                await transaction.RollbackAsync();
                                return (false, null, "Each authority must have either an AuthorityId or both CustomName and CustomEmail");
                            }

                            if (hasPredefined && hasCustom)
                            {
                                await transaction.RollbackAsync();
                                return (false, null, "Authority cannot have both AuthorityId and custom fields");
                            }

                            if (hasPredefined)
                            {
                                bool authorityExists = await context.Authorities
                                    .AnyAsync(a => a.Id == authorityInput.AuthorityId && a.IsActive);

                                if (!authorityExists)
                                {
                                    await transaction.RollbackAsync();
                                    return (false, null, $"Authority with ID {authorityInput.AuthorityId} not found or is inactive");
                                }
                            }

                            IssueAuthority issueAuthority = new()
                            {
                                Id = Guid.NewGuid(),
                                IssueId = issue.Id,
                                AuthorityId = authorityInput.AuthorityId,
                                CustomName = hasPredefined ? null : authorityInput.CustomName,
                                CustomEmail = hasPredefined ? null : authorityInput.CustomEmail,
                                CreatedAt = DateTime.UtcNow
                            };

                            context.IssueAuthorities.Add(issueAuthority);
                        }
                    }
                    hasRelatedChanges = true;
                }

                // Use EF Core change tracker to detect if the issue entity was modified
                bool hasEntityChanges = context.Entry(issue).State == EntityState.Modified;

                // Only proceed if there were actual changes
                if (!hasEntityChanges && !hasRelatedChanges)
                {
                    await transaction.RollbackAsync();
                    return (false, null, "No changes provided");
                }

                // Set status to UnderReview for admin re-approval
                issue.Status = IssueStatus.UnderReview;
                issue.UpdatedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                // Reload the issue with all relations BEFORE committing
                // This ensures we can rollback if the reload fails
                await context.Entry(issue).Reference(i => i.User).LoadAsync();
                await context.Entry(issue).Collection(i => i.Photos).LoadAsync();
                await context.Entry(issue).Collection(i => i.IssueAuthorities).LoadAsync();

                // Load Authority for each IssueAuthority
                foreach (IssueAuthority ia in issue.IssueAuthorities.Where(ia => ia.AuthorityId.HasValue))
                {
                    await context.Entry(ia).Reference(x => x.Authority).LoadAsync();
                }

                // Build response before committing
                IssueDetailResponse response = new()
                {
                    Id = issue.Id,
                    Title = issue.Title,
                    Description = issue.Description,
                    Category = issue.Category,
                    Address = issue.Address,
                    Latitude = issue.Latitude,
                    Longitude = issue.Longitude,
                    District = issue.District,
                    Urgency = issue.Urgency,
                    Status = issue.Status,
                    EmailsSent = issue.EmailsSent,
                    CommunityVotes = issue.CommunityVotes,
                    HasVoted = null, // Owner's own issue - voting not applicable
                    DesiredOutcome = issue.DesiredOutcome,
                    CommunityImpact = issue.CommunityImpact,
                    CreatedAt = issue.CreatedAt,
                    UpdatedAt = issue.UpdatedAt,
                    Photos = issue.Photos.Select(p => new IssuePhotoResponse
                    {
                        Id = p.Id,
                        Url = p.Url,
                        Description = p.Description,
                        IsPrimary = p.IsPrimary,
                        CreatedAt = p.CreatedAt
                    }).ToList(),
                    Authorities = issue.IssueAuthorities.Select(ia => new IssueAuthorityResponse
                    {
                        AuthorityId = ia.AuthorityId,
                        Name = ia.Authority?.Name ?? ia.CustomName ?? string.Empty,
                        Email = ia.Authority?.Email ?? ia.CustomEmail ?? string.Empty,
                        IsPredefined = ia.AuthorityId.HasValue
                    }).ToList(),
                    User = issue.User is not null
                        ? new UserBasicResponse
                        {
                            Id = issue.User.Id,
                            Name = issue.User.DisplayName,
                            PhotoUrl = issue.User.PhotoUrl
                        }
                        : new UserBasicResponse
                        {
                            Id = Guid.Empty,
                            Name = "Deleted User",
                            PhotoUrl = null
                        }
                };

                // Commit only after everything is ready
                await transaction.CommitAsync();

                logger.LogInformation("Issue {IssueId} updated and set to UnderReview by user {UserId}", issueId, userProfile.Id);

                return (true, response, null);
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
                return (false, "User not found");
            if (user.IsDeleted)
                return (false, "This account has been deleted.");

            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return (false, "Issue not found");
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

                // Check if this vote was already created in a previous retry attempt
                IssueVote? existingVote = await context.IssueVotes
                    .FirstOrDefaultAsync(v => v.Id == voteId);

                if (existingVote != null)
                {
                    // Vote was created on a previous attempt - treat as success
                    return true;
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
                    return false;
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

                return true;
            });

            if (!votedByThisRequest)
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
                return (false, "User not found");
            if (user.IsDeleted)
                return (false, "This account has been deleted.");

            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return (false, "Issue not found");
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

            var removedByThisRequest = await strategy.ExecuteAsync(async () =>
            {
                // Clear change tracker to ensure clean state on retry
                context.ChangeTracker.Clear();

                await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

                // Use ExecuteDeleteAsync for atomic delete - avoids entity tracking issues on retry
                var rowsDeleted = await context.IssueVotes
                    .Where(v => v.IssueId == issueId && v.UserId == user.Id)
                    .ExecuteDeleteAsync();

                // If no rows deleted, vote was already removed (concurrent request or retry after success)
                if (rowsDeleted == 0)
                {
                    await transaction.RollbackAsync();
                    return false; // Idempotent success
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
                return true;
            });

            if (removedByThisRequest)
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