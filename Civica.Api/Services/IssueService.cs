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
    public async Task<PagedResult<IssueListResponse>> GetAllIssuesAsync(GetIssuesRequest request)
    {
        try
        {
            IQueryable<Issue> query = context.Issues
                .Include(i => i.Photos)
                .Include(i => i.User)
                .Where(i => i.Status == IssueStatus.Approved && i.PublicVisibility)
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
                List<IssuePhoto> photos = request.PhotoUrls.Select((url, index) => new IssuePhoto
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
                // Check for duplicate AuthorityIds
                var predefinedIds = request.Authorities
                    .Where(a => a.AuthorityId.HasValue)
                    .Select(a => a.AuthorityId!.Value)
                    .ToList();

                if (predefinedIds.Count != predefinedIds.Distinct().Count())
                {
                    throw new InvalidOperationException("Duplicate authority IDs are not allowed");
                }

                // Check for duplicate custom emails (only for custom authorities, not predefined)
                var customEmails = request.Authorities
                    .Where(a => !a.AuthorityId.HasValue && !string.IsNullOrWhiteSpace(a.CustomEmail))
                    .Select(a => a.CustomEmail!.ToLowerInvariant())
                    .ToList();

                if (customEmails.Count != customEmails.Distinct().Count())
                {
                    throw new InvalidOperationException("Duplicate custom authority emails are not allowed");
                }

                foreach (var authorityInput in request.Authorities)
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

            await context.SaveChangesAsync();

            // Award points for creating an issue
            await gamificationService.AwardPointsAsync(
                userProfile.Id, 
                50, 
                "Reported a new community issue");

            // Check for achievements
            await gamificationService.CheckAndAwardAchievementsAsync(userProfile.Id);

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
                return (false, "Please wait before confirming another email for this issue");
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

            // Only allow incrementing for approved, publicly visible issues
            if (issueInfo.Status != IssueStatus.Approved || !issueInfo.PublicVisibility)
            {
                logger.LogWarning("Attempt to increment email count for non-public issue {IssueId}", issueId);
                return (false, "Issue is not publicly available");
            }

            // Atomic increment using ExecuteUpdateAsync to prevent race conditions
            int rowsAffected = await context.Issues
                .Where(i => i.Id == issueId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(i => i.EmailsSent, i => i.EmailsSent + 1)
                    .SetProperty(i => i.UpdatedAt, DateTime.UtcNow));

            if (rowsAffected == 0)
            {
                logger.LogWarning("Failed to increment email count for issue {IssueId}", issueId);
                return (false, "Failed to update issue");
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
}