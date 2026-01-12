using Civica.Api.Services.Interfaces;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Models.Responses.Authority;
using Civica.Api.Models.Domain;
using Civica.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Civica.Api.Services;

public class IssueService(
    ILogger<IssueService> logger,
    CivicaDbContext context,
    IGamificationService gamificationService,
    IMemoryCache memoryCache)
    : IIssueService
{
    private static readonly TimeSpan EmailCooldownDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// Error message returned when rate limited. Used by endpoint to detect 429 response.
    /// </summary>
    public const string RateLimitedError = "RATE_LIMITED";
    public async Task<PagedResult<IssueListResponse>> GetAllIssuesAsync(GetIssuesRequest request)
    {
        try
        {
            IQueryable<Issue> query = context.Issues
                .Include(i => i.Photos)
                .Include(i => i.User)
                .Where(i => i.Status == IssueStatus.Active && i.PublicVisibility)
                .AsQueryable();

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

            // Apply sorting
            query = request.SortBy?.ToLower() switch
            {
                "emails" => request.SortDescending ? 
                    query.OrderByDescending(i => i.EmailsSent) : 
                    query.OrderBy(i => i.EmailsSent),
                "urgency" => request.SortDescending ?
                    query.OrderByDescending(i => i.Urgency) :
                    query.OrderBy(i => i.Urgency),
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
                    CreatedAt = i.CreatedAt,
                    MainPhotoUrl = i.Photos
                        .Where(p => p.IsPrimary || i.Photos.Count == 1)
                        .OrderBy(p => p.CreatedAt)
                        .Select(p => p.Url)
                        .FirstOrDefault(),
                    Neighborhood = i.Neighborhood,
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all issues");
            throw;
        }
    }

    public async Task<IssueDetailResponse?> GetIssueByIdAsync(Guid id)
    {
        try
        {
            Issue? issue = await context.Issues
                .Include(i => i.Photos)
                .Include(i => i.User)
                .Include(i => i.IssueAuthorities)
                    .ThenInclude(ia => ia.Authority)
                .Where(i => i.Id == id && i.PublicVisibility)
                .FirstOrDefaultAsync();

            if (issue == null)
            {
                logger.LogWarning("Issue {IssueId} not found or not publicly visible", id);
                return null;
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
                Neighborhood = issue.Neighborhood,
                District = issue.District,
                Landmark = issue.Landmark,
                Urgency = issue.Urgency,
                EstimatedImpact = issue.EstimatedImpact,
                Tags = issue.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                Status = issue.Status,
                EmailsSent = issue.EmailsSent,
                CurrentSituation = issue.CurrentSituation,
                DesiredOutcome = issue.DesiredOutcome,
                CommunityImpact = issue.CommunityImpact,
                AIGeneratedDescription = issue.AIGeneratedDescription,
                AIProposedSolution = issue.AIProposedSolution,
                PublicVisibility = issue.PublicVisibility,
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
                User = new UserBasicResponse
                {
                    Id = issue.User.Id,
                    Name = issue.User.DisplayName,
                    PhotoUrl = issue.User.PhotoUrl
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
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Clear change tracker to ensure fresh data on retry (prevents double-incrementing counters)
            context.ChangeTracker.Clear();

            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
            // Get user profile
            UserProfile? userProfile = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (userProfile == null)
            {
                throw new InvalidOperationException($"User profile not found for Supabase ID: {supabaseUserId}");
            }

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
                LocationAccuracy = request.LocationAccuracy,
                Neighborhood = request.Neighborhood,
                District = request.District,
                Landmark = request.Landmark,
                Urgency = request.Urgency,
                EstimatedImpact = request.EstimatedImpact,
                Tags = request.Tags != null ? string.Join(",", request.Tags) : null,
                CurrentSituation = request.CurrentSituation,
                DesiredOutcome = request.DesiredOutcome,
                CommunityImpact = request.CommunityImpact,
                AIGeneratedDescription = request.AIGeneratedDescription,
                AIProposedSolution = request.AIProposedSolution,
                AIConfidence = request.AIConfidence,
                Status = IssueStatus.Submitted,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Issues.Add(issue);

            // Add photos if provided
            if (request.PhotoUrls != null && request.PhotoUrls.Any())
            {
                var validPhotoUrls = request.PhotoUrls
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
                var authorities = request.Authorities.Where(a => a != null).ToList();

                // Check for duplicate AuthorityIds
                var predefinedIds = authorities
                    .Where(a => a.AuthorityId.HasValue)
                    .Select(a => a.AuthorityId!.Value)
                    .ToList();

                if (predefinedIds.Count != predefinedIds.Distinct().Count())
                {
                    throw new InvalidOperationException("Duplicate authority IDs are not allowed");
                }

                // Check for duplicate custom emails (only for custom authorities, not predefined)
                var customEmails = authorities
                    .Where(a => !a.AuthorityId.HasValue && !string.IsNullOrWhiteSpace(a.CustomEmail))
                    .Select(a => a.CustomEmail!.ToLowerInvariant())
                    .ToList();

                if (customEmails.Count != customEmails.Distinct().Count())
                {
                    throw new InvalidOperationException("Duplicate custom authority emails are not allowed");
                }

                foreach (var authorityInput in authorities)
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
                "issues_reported",
                1);

            // Update quality_photos achievement if issue has 3+ photos
            // Use incremental progress (not absolute) to avoid race conditions with concurrent issue creations
            var photoCount = request.PhotoUrls?.Count(url => !string.IsNullOrWhiteSpace(url)) ?? 0;
            if (photoCount >= 3)
            {
                await gamificationService.UpdateAchievementProgressAsync(
                    userProfile.Id,
                    "quality_photos",
                    1);
            }

            // Check for badge eligibility based on new stats
            await gamificationService.CheckAndAwardBadgesAsync(userProfile.Id);

            await transaction.CommitAsync();

            logger.LogInformation("Issue {IssueId} created successfully by user {UserId}", 
                issue.Id, userProfile.Id);

            return new CreateIssueResponse
            {
                Id = issue.Id,
                Status = issue.Status.ToString(),
                CreatedAt = issue.CreatedAt
            };
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
            var issueInfo = await context.Issues
                .Where(i => i.Id == issueId)
                .Select(i => new { i.Status, i.PublicVisibility })
                .FirstOrDefaultAsync();

            if (issueInfo == null)
            {
                logger.LogWarning("Issue {IssueId} not found", issueId);
                return (false, "Issue not found");
            }

            // Only allow incrementing for active, publicly visible issues
            if (issueInfo.Status != IssueStatus.Active || !issueInfo.PublicVisibility)
            {
                logger.LogWarning("Attempt to increment email count for non-public issue {IssueId}", issueId);
                return (false, "Issue is not publicly available");
            }

            // Atomic increment with status/visibility check to prevent TOCTOU race condition
            int rowsAffected = await context.Issues
                .Where(i => i.Id == issueId
                         && i.Status == IssueStatus.Active
                         && i.PublicVisibility)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(i => i.EmailsSent, i => i.EmailsSent + 1)
                    .SetProperty(i => i.UpdatedAt, DateTime.UtcNow));

            if (rowsAffected == 0)
            {
                // Issue may have been unapproved/hidden between check and update
                logger.LogWarning("Failed to increment email count for issue {IssueId} - may have been modified", issueId);
                return (false, "Issue is no longer publicly available");
            }

            // Set cooldown in cache
            memoryCache.Set(cacheKey, true, EmailCooldownDuration);

            logger.LogInformation("Email count incremented for issue {IssueId}", issueId);

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
            // Get user profile
            UserProfile? userProfile = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (userProfile == null)
            {
                return new PagedResult<IssueListResponse>
                {
                    Items = [],
                    TotalItems = 0,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = 0
                };
            }

            IQueryable<Issue> query = context.Issues
                .Include(i => i.Photos)
                .Include(i => i.User)
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
                    CreatedAt = i.CreatedAt,
                    MainPhotoUrl = i.Photos
                        .Where(p => p.IsPrimary || i.Photos.Count == 1)
                        .OrderBy(p => p.CreatedAt)
                        .Select(p => p.Url)
                        .FirstOrDefault(),
                    Neighborhood = i.Neighborhood,
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
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Clear change tracker to ensure fresh data on retry (prevents skipping gamification on retry)
            context.ChangeTracker.Clear();

            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Get user profile
                UserProfile? userProfile = await context.UserProfiles
                    .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

                if (userProfile == null)
                {
                    return (false, "User profile not found");
                }

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
                var previousStatus = issue.Status;
                issue.Status = request.Status;
                issue.UpdatedAt = DateTime.UtcNow;

                // If status changed to Resolved, update gamification for the issue OWNER (not the caller)
                if (request.Status == IssueStatus.Resolved && previousStatus != IssueStatus.Resolved)
                {
                    // Get the issue owner's profile to update their stats
                    UserProfile? issueOwner = issue.UserId == userProfile.Id
                        ? userProfile
                        : await context.UserProfiles.FindAsync(issue.UserId);

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
                        "issues_resolved",
                        1);

                    // Check for badge eligibility
                    await gamificationService.CheckAndAwardBadgesAsync(issue.UserId);
                }

                await transaction.CommitAsync();

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
        var allowedUserStatuses = new[] { IssueStatus.Cancelled, IssueStatus.Resolved };

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
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<(bool Success, IssueDetailResponse? Issue, string? Error)>(async () =>
        {
            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Get user profile
                UserProfile? userProfile = await context.UserProfiles
                    .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

                if (userProfile == null)
                {
                    return (false, null, "User profile not found");
                }

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

                if (request.LocationAccuracy.HasValue)
                    issue.LocationAccuracy = request.LocationAccuracy.Value;

                if (request.Neighborhood != null)
                    issue.Neighborhood = request.Neighborhood;

                if (request.Landmark != null)
                    issue.Landmark = request.Landmark;

                if (request.Urgency.HasValue)
                    issue.Urgency = request.Urgency.Value;

                if (request.EstimatedImpact.HasValue)
                    issue.EstimatedImpact = request.EstimatedImpact.Value;

                if (request.Tags != null)
                    issue.Tags = request.Tags.Any() ? string.Join(",", request.Tags) : null;

                if (request.CurrentSituation != null)
                    issue.CurrentSituation = request.CurrentSituation;

                if (request.DesiredOutcome != null)
                    issue.DesiredOutcome = request.DesiredOutcome;

                if (request.CommunityImpact != null)
                    issue.CommunityImpact = request.CommunityImpact;

                if (request.AIGeneratedDescription != null)
                    issue.AIGeneratedDescription = request.AIGeneratedDescription;

                if (request.AIProposedSolution != null)
                    issue.AIProposedSolution = request.AIProposedSolution;

                if (request.AIConfidence.HasValue)
                    issue.AIConfidence = request.AIConfidence.Value;

                // Handle photo updates (replace all if provided)
                if (request.PhotoUrls != null)
                {
                    context.IssuePhotos.RemoveRange(issue.Photos);

                    var validPhotoUrls = request.PhotoUrls
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
                        var authorities = request.Authorities.Where(a => a != null).ToList();

                        // Check for duplicate AuthorityIds
                        var predefinedIds = authorities
                            .Where(a => a.AuthorityId.HasValue)
                            .Select(a => a.AuthorityId!.Value)
                            .ToList();

                        if (predefinedIds.Count != predefinedIds.Distinct().Count())
                        {
                            await transaction.RollbackAsync();
                            return (false, null, "Duplicate authority IDs are not allowed");
                        }

                        // Check for duplicate custom emails
                        var customEmails = authorities
                            .Where(a => !a.AuthorityId.HasValue && !string.IsNullOrWhiteSpace(a.CustomEmail))
                            .Select(a => a.CustomEmail!.ToLowerInvariant())
                            .ToList();

                        if (customEmails.Count != customEmails.Distinct().Count())
                        {
                            await transaction.RollbackAsync();
                            return (false, null, "Duplicate custom authority emails are not allowed");
                        }

                        foreach (var authorityInput in authorities)
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
                foreach (var ia in issue.IssueAuthorities.Where(ia => ia.AuthorityId.HasValue))
                {
                    await context.Entry(ia).Reference(x => x.Authority).LoadAsync();
                }

                // Build response before committing
                var response = new IssueDetailResponse
                {
                    Id = issue.Id,
                    Title = issue.Title,
                    Description = issue.Description,
                    Category = issue.Category,
                    Address = issue.Address,
                    Latitude = issue.Latitude,
                    Longitude = issue.Longitude,
                    Neighborhood = issue.Neighborhood,
                    District = issue.District,
                    Landmark = issue.Landmark,
                    Urgency = issue.Urgency,
                    EstimatedImpact = issue.EstimatedImpact,
                    Tags = issue.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Status = issue.Status,
                    EmailsSent = issue.EmailsSent,
                    CurrentSituation = issue.CurrentSituation,
                    DesiredOutcome = issue.DesiredOutcome,
                    CommunityImpact = issue.CommunityImpact,
                    AIGeneratedDescription = issue.AIGeneratedDescription,
                    AIProposedSolution = issue.AIProposedSolution,
                    PublicVisibility = issue.PublicVisibility,
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
                    User = new UserBasicResponse
                    {
                        Id = issue.User.Id,
                        Name = issue.User.DisplayName,
                        PhotoUrl = issue.User.PhotoUrl
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
}