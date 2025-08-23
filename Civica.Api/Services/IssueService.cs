using Civica.Api.Services.Interfaces;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Models.Domain;
using Civica.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Civica.Api.Services;

public class IssueService(
    ILogger<IssueService> logger,
    CivicaDbContext context,
    IGamificationService gamificationService)
    : IIssueService
{
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
                query = query.Where(i => i.District != null && 
                    i.District.Contains(request.District, StringComparison.OrdinalIgnoreCase));
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

            return new()
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
                .Where(i => i.Id == id && i.PublicVisibility)
                .FirstOrDefaultAsync();

            if (issue == null)
            {
                logger.LogWarning("Issue {IssueId} not found or not publicly visible", id);
                return null;
            }

            return new()
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
                AuthorityEmail = issue.AuthorityEmail,
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
                User = new()
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
                AuthorityEmail = request.AuthorityEmail,
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

            return new()
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

    public async Task<bool> TrackEmailSentAsync(Guid issueId, TrackEmailRequest request, string supabaseUserId)
    {
        using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        
        try
        {
            // Get user profile
            UserProfile? userProfile = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (userProfile == null)
            {
                logger.LogWarning("User profile not found for Supabase ID: {SupabaseUserId}", supabaseUserId);
                return false;
            }

            // Get the issue
            Issue? issue = await context.Issues
                .Include(i => i.EmailTrackings)
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                logger.LogWarning("Issue {IssueId} not found", issueId);
                return false;
            }

            // Check if user already sent email for this issue
            EmailTracking? existingTracking = issue.EmailTrackings
                .FirstOrDefault(e => e.UserId == userProfile.Id);

            if (existingTracking != null)
            {
                logger.LogWarning("User {UserId} already sent email for issue {IssueId}", 
                    userProfile.Id, issueId);
                return false;
            }

            // Create email tracking record
            EmailTracking emailTracking = new()
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                UserId = userProfile.Id,
                EmailAddress = request.EmailAddress,
                SentAt = DateTime.UtcNow,
                EmailSubject = request.Subject,
                EmailBody = request.Body,
                RecipientType = request.RecipientType ?? "authority",
                TrackingStatus = "sent"
            };

            context.EmailTrackings.Add(emailTracking);

            // Update issue email count
            issue.EmailsSent++;
            issue.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            // Award points for sending email
            await gamificationService.AwardPointsAsync(
                userProfile.Id, 
                25, 
                "Sent email to authorities about an issue");

            // Check for achievements
            await gamificationService.CheckAndAwardAchievementsAsync(userProfile.Id);

            await transaction.CommitAsync();

            logger.LogInformation("Email tracking recorded for issue {IssueId} by user {UserId}", 
                issueId, userProfile.Id);

            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error tracking email for issue {IssueId}", issueId);
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
                return new()
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

            return new()
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