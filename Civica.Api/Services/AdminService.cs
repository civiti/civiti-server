using Civica.Api.Services.Interfaces;
using Civica.Api.Models.Requests.Admin;
using Civica.Api.Models.Responses.Admin;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Models.Responses.Authority;
using Civica.Api.Models.Domain;
using Civica.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Civica.Api.Services;

public class AdminService(
    ILogger<AdminService> logger,
    CivicaDbContext context,
    IGamificationService gamificationService)
    : IAdminService
{
    public async Task<PagedResult<AdminIssueResponse>> GetPendingIssuesAsync(GetPendingIssuesRequest request)
    {
        try
        {
            IQueryable<Issue> query = context.Issues
                .Include(i => i.User)
                .Include(i => i.Photos)
                .Where(i => i.Status == IssueStatus.Submitted || i.Status == IssueStatus.UnderReview)
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

            if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                var searchLower = request.SearchTerm.ToLower();
                query = query.Where(i => 
                    i.Title.ToLower().Contains(searchLower) ||
                    i.Description.ToLower().Contains(searchLower) ||
                    i.Address.ToLower().Contains(searchLower));
            }

            if (request.SubmittedAfter.HasValue)
            {
                query = query.Where(i => i.CreatedAt >= request.SubmittedAfter.Value);
            }

            if (request.SubmittedBefore.HasValue)
            {
                query = query.Where(i => i.CreatedAt <= request.SubmittedBefore.Value);
            }

            // Count before pagination
            var totalCount = await query.CountAsync();

            // Apply sorting
            query = request.SortBy?.ToLower() switch
            {
                "title" => request.SortDescending ? query.OrderByDescending(i => i.Title) : query.OrderBy(i => i.Title),
                "category" => request.SortDescending ? query.OrderByDescending(i => i.Category) : query.OrderBy(i => i.Category),
                "urgency" => request.SortDescending ? query.OrderByDescending(i => i.Urgency) : query.OrderBy(i => i.Urgency),
                "updatedat" => request.SortDescending ? query.OrderByDescending(i => i.UpdatedAt) : query.OrderBy(i => i.UpdatedAt),
                _ => request.SortDescending ? query.OrderByDescending(i => i.CreatedAt) : query.OrderBy(i => i.CreatedAt)
            };

            // Apply pagination
            List<AdminIssueResponse> items = await query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(i => new AdminIssueResponse
                {
                    Id = i.Id,
                    Title = i.Title,
                    Category = i.Category,
                    Urgency = i.Urgency,
                    Priority = i.Priority,
                    Status = i.Status,
                    Address = i.Address,
                    CreatedAt = i.CreatedAt,
                    PhotoCount = i.Photos.Count,
                    EmailsSent = i.EmailsSent,
                    UserName = i.User.DisplayName
                })
                .ToListAsync();

            return new PagedResult<AdminIssueResponse>
            {
                Items = items,
                TotalItems = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting pending issues");
            throw;
        }
    }

    public async Task<AdminIssueDetailResponse?> GetIssueDetailsForAdminAsync(Guid issueId)
    {
        try
        {
            Issue? issue = await context.Issues
                .Include(i => i.User)
                .Include(i => i.Photos)
                .Include(i => i.AdminActions)
                    .ThenInclude(aa => aa.AdminUser)
                .Include(i => i.IssueAuthorities)
                    .ThenInclude(ia => ia.Authority)
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return null;
            }

            var userResolvedIssues = await context.Issues
                .CountAsync(i => i.UserId == issue.UserId && i.Status == IssueStatus.Resolved);

            return new AdminIssueDetailResponse
            {
                Id = issue.Id,
                Title = issue.Title,
                Description = issue.Description,
                Category = issue.Category,
                Urgency = issue.Urgency,
                Priority = issue.Priority,
                Status = issue.Status,
                Address = issue.Address,
                Latitude = issue.Latitude,
                Longitude = issue.Longitude,
                LocationAccuracy = issue.LocationAccuracy,
                Neighborhood = issue.Neighborhood,
                District = issue.District,
                Landmark = issue.Landmark,
                EstimatedImpact = issue.EstimatedImpact,
                Tags = issue.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                CurrentSituation = issue.CurrentSituation,
                DesiredOutcome = issue.DesiredOutcome,
                CommunityImpact = issue.CommunityImpact,
                AIGeneratedDescription = issue.AIGeneratedDescription,
                AIProposedSolution = issue.AIProposedSolution,
                AIConfidence = issue.AIConfidence,
                AdminNotes = issue.AdminNotes,
                RejectionReason = issue.RejectionReason,
                AssignedDepartment = issue.AssignedDepartment,
                EstimatedResolutionTime = issue.EstimatedResolutionTime,
                PublicVisibility = issue.PublicVisibility,
                ReviewedAt = issue.ReviewedAt,
                ReviewedBy = issue.ReviewedBy,
                CreatedAt = issue.CreatedAt,
                UpdatedAt = issue.UpdatedAt,
                UserId = issue.UserId,
                UserName = issue.User.DisplayName,
                UserEmail = issue.User.Email,
                UserPhone = issue.User.Phone,
                UserTotalIssues = await context.Issues.CountAsync(i => i.UserId == issue.UserId),
                UserResolvedIssues = userResolvedIssues,
                UserPoints = issue.User.Points,
                Photos = issue.Photos.Select(p => new AdminIssuePhotoResponse
                {
                    Id = p.Id,
                    Url = p.Url,
                    ThumbnailUrl = p.ThumbnailUrl,
                    Description = p.Description,
                    IsPrimary = p.IsPrimary,
                    FileSize = p.FileSize,
                    CreatedAt = p.CreatedAt
                }).ToList(),
                Authorities = issue.IssueAuthorities.Select(ia => new IssueAuthorityResponse
                {
                    AuthorityId = ia.AuthorityId,
                    Name = ia.Authority?.Name ?? ia.CustomName ?? string.Empty,
                    Email = ia.Authority?.Email ?? ia.CustomEmail ?? string.Empty,
                    IsPredefined = ia.AuthorityId.HasValue
                }).ToList(),
                AdminActions = issue.AdminActions.Select(aa => new AdminActionResponse
                {
                    Id = aa.Id,
                    IssueId = aa.IssueId,
                    IssueTitle = issue.Title,
                    AdminUserId = aa.AdminUserId,
                    AdminName = aa.AdminUser?.DisplayName ?? "System",
                    AdminEmail = aa.AdminUser?.Email ?? "system@civica.ro",
                    ActionType = aa.ActionType,
                    Notes = aa.Notes,
                    PreviousStatus = aa.PreviousStatus,
                    NewStatus = aa.NewStatus,
                    AssignedDepartment = aa.AssignedDepartment,
                    EstimatedResolutionTime = aa.EstimatedResolutionTime,
                    CreatedAt = aa.CreatedAt
                }).ToList(),
                EmailsSent = issue.EmailsSent
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting issue details for admin: {IssueId}", issueId);
            throw;
        }
    }

    public async Task<IssueActionResponse> ApproveIssueAsync(Guid issueId, ApproveIssueRequest request, string adminUserId)
    {
        using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        
        try
        {
            Issue? issue = await context.Issues
                .Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Issue not found"
                };
            }

            if (issue.Status != IssueStatus.Submitted && issue.Status != IssueStatus.UnderReview)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Issue is not in a reviewable state"
                };
            }

            // Get admin user
            UserProfile? adminUser = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == adminUserId);

            if (adminUser == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Admin user not found"
                };
            }

            // Update issue
            var previousStatus = issue.Status.ToString();
            issue.Status = IssueStatus.Approved;
            issue.ReviewedAt = DateTime.UtcNow;
            issue.ReviewedBy = adminUser.DisplayName;
            issue.AdminNotes = request.AdminNotes;
            issue.Priority = request.Priority ?? issue.Priority;
            issue.AssignedDepartment = request.AssignedDepartment;
            issue.EstimatedResolutionTime = request.EstimatedResolutionTime;
            issue.UpdatedAt = DateTime.UtcNow;

            // Create admin action record
            AdminAction adminAction = new()
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                AdminUserId = adminUser.Id,
                AdminSupabaseId = adminUserId,
                ActionType = AdminActionType.Approve,
                Notes = request.AdminNotes,
                PreviousStatus = previousStatus,
                NewStatus = IssueStatus.Approved.ToString(),
                AssignedDepartment = request.AssignedDepartment,
                EstimatedResolutionTime = request.EstimatedResolutionTime,
                CreatedAt = DateTime.UtcNow
            };

            context.AdminActions.Add(adminAction);

            // Award points to user
            await gamificationService.AwardPointsAsync(issue.UserId, 100, $"Issue approved: {issue.Title}");
            
            // Update achievement progress
            await gamificationService.UpdateAchievementProgressAsync(issue.UserId, "issues_approved", 1);
            
            // Check for new badges
            await gamificationService.CheckAndAwardBadgesAsync(issue.UserId);

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            logger.LogInformation(
                "Issue approved: {IssueId} by admin: {AdminUserId}",
                issueId,
                adminUserId);

            return new IssueActionResponse
            {
                Success = true,
                Message = "Issue approved successfully",
                IssueId = issueId,
                NewStatus = IssueStatus.Approved.ToString(),
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error approving issue: {IssueId}", issueId);
            
            return new IssueActionResponse
            {
                Success = false,
                Message = "An error occurred while approving the issue"
            };
        }
    }

    public async Task<IssueActionResponse> RejectIssueAsync(Guid issueId, RejectIssueRequest request, string adminUserId)
    {
        using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        
        try
        {
            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Issue not found"
                };
            }

            if (issue.Status != IssueStatus.Submitted && issue.Status != IssueStatus.UnderReview)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Issue is not in a reviewable state"
                };
            }

            // Get admin user
            UserProfile? adminUser = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == adminUserId);

            if (adminUser == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Admin user not found"
                };
            }

            // Update issue
            var previousStatus = issue.Status.ToString();
            issue.Status = IssueStatus.Rejected;
            issue.ReviewedAt = DateTime.UtcNow;
            issue.ReviewedBy = adminUser.DisplayName;
            issue.RejectionReason = request.Reason;
            issue.AdminNotes = request.InternalNotes;
            issue.UpdatedAt = DateTime.UtcNow;

            // Create admin action record
            AdminAction adminAction = new()
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                AdminUserId = adminUser.Id,
                AdminSupabaseId = adminUserId,
                ActionType = AdminActionType.Reject,
                Notes = $"Reason: {request.Reason}. Internal notes: {request.InternalNotes}",
                PreviousStatus = previousStatus,
                NewStatus = IssueStatus.Rejected.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            context.AdminActions.Add(adminAction);

            // No points awarded for rejection, but update stats
            await gamificationService.UpdateAchievementProgressAsync(issue.UserId, "issues_rejected", 1);

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            logger.LogInformation(
                "Issue rejected: {IssueId} by admin: {AdminUserId}. Reason: {Reason}",
                issueId,
                adminUserId,
                request.Reason);

            return new IssueActionResponse
            {
                Success = true,
                Message = "Issue rejected",
                IssueId = issueId,
                NewStatus = IssueStatus.Rejected.ToString(),
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error rejecting issue: {IssueId}", issueId);
            
            return new IssueActionResponse
            {
                Success = false,
                Message = "An error occurred while rejecting the issue"
            };
        }
    }

    public async Task<IssueActionResponse> RequestChangesAsync(Guid issueId, RequestChangesRequest request, string adminUserId)
    {
        using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        
        try
        {
            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Issue not found"
                };
            }

            // Get admin user
            UserProfile? adminUser = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == adminUserId);

            if (adminUser == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = "Admin user not found"
                };
            }

            // Update issue
            var previousStatus = issue.Status.ToString();
            issue.Status = IssueStatus.UnderReview;
            issue.AdminNotes = request.RequestedChanges;
            issue.UpdatedAt = DateTime.UtcNow;

            // Create admin action record
            AdminAction adminAction = new()
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                AdminUserId = adminUser.Id,
                AdminSupabaseId = adminUserId,
                ActionType = AdminActionType.RequestChanges,
                Notes = request.RequestedChanges,
                PreviousStatus = previousStatus,
                NewStatus = IssueStatus.UnderReview.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            context.AdminActions.Add(adminAction);

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            logger.LogInformation(
                "Changes requested for issue: {IssueId} by admin: {AdminUserId}",
                issueId,
                adminUserId);

            return new IssueActionResponse
            {
                Success = true,
                Message = "Changes requested successfully",
                IssueId = issueId,
                NewStatus = IssueStatus.UnderReview.ToString(),
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error requesting changes for issue: {IssueId}", issueId);
            
            return new IssueActionResponse
            {
                Success = false,
                Message = "An error occurred while requesting changes"
            };
        }
    }

    public async Task<AdminStatisticsResponse> GetStatisticsAsync(string period = "30d")
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            DateTime startDate = period switch
            {
                "7d" => now.AddDays(-7),
                "30d" => now.AddDays(-30),
                "90d" => now.AddDays(-90),
                "1y" => now.AddYears(-1),
                _ => DateTime.MinValue
            };

            DateTime today = now.Date;
            DateTime weekStart = now.AddDays(-(int)now.DayOfWeek).Date;
            DateTime monthStart = new(now.Year, now.Month, 1);

            // Get all issues for counting
            List<Issue> allIssues = await context.Issues.ToListAsync();
            List<Issue> periodIssues = startDate != DateTime.MinValue 
                ? allIssues.Where(i => i.CreatedAt >= startDate).ToList()
                : allIssues;

            // Count by status
            Dictionary<IssueStatus, int> statusCounts = periodIssues
                .GroupBy(i => i.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            // Count submissions by time
            var submissionsToday = allIssues.Count(i => i.CreatedAt.Date == today);
            var submissionsThisWeek = allIssues.Count(i => i.CreatedAt >= weekStart);
            var submissionsThisMonth = allIssues.Count(i => i.CreatedAt >= monthStart);

            // Admin activity
            List<AdminAction> adminActions = await context.AdminActions
                .Where(aa => aa.CreatedAt >= startDate)
                .ToListAsync();

            var reviewedToday = adminActions.Count(aa => aa.CreatedAt.Date == today);
            var reviewedThisWeek = adminActions.Count(aa => aa.CreatedAt >= weekStart);
            var reviewedThisMonth = adminActions.Count(aa => aa.CreatedAt >= monthStart);

            // Calculate average review time
            List<Issue> reviewedIssues = await context.Issues
                .Where(i => i.ReviewedAt.HasValue && i.CreatedAt >= startDate)
                .ToListAsync();

            var avgReviewTime = reviewedIssues.Any()
                ? reviewedIssues.Average(i => (i.ReviewedAt!.Value - i.CreatedAt).TotalHours)
                : 0;

            // Category breakdown
            Dictionary<string, int> categoryBreakdown = periodIssues
                .GroupBy(i => i.Category.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            Dictionary<string, int> urgencyBreakdown = periodIssues
                .GroupBy(i => i.Urgency.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            Dictionary<string, int> priorityBreakdown = periodIssues
                .GroupBy(i => i.Priority.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // User statistics
            var totalUsers = await context.UserProfiles.CountAsync();
            var activeUsersThisMonth = await context.Issues
                .Where(i => i.CreatedAt >= monthStart)
                .Select(i => i.UserId)
                .Distinct()
                .CountAsync();

            var totalEmailsSent = await context.Issues.SumAsync(i => i.EmailsSent);

            // Performance metrics
            var totalIssues = periodIssues.Count;
            var approvedCount = statusCounts.GetValueOrDefault(IssueStatus.Approved, 0);
            var rejectedCount = statusCounts.GetValueOrDefault(IssueStatus.Rejected, 0);
            var resolvedCount = statusCounts.GetValueOrDefault(IssueStatus.Resolved, 0);
            var pendingCount = statusCounts.GetValueOrDefault(IssueStatus.Submitted, 0) + 
                              statusCounts.GetValueOrDefault(IssueStatus.UnderReview, 0);

            var approvalRate = totalIssues > 0 
                ? (double)approvedCount / (approvedCount + rejectedCount) * 100 
                : 0;
            
            var resolutionRate = approvedCount > 0 
                ? (double)resolvedCount / approvedCount * 100 
                : 0;

            return new AdminStatisticsResponse
            {
                TotalSubmissions = totalIssues,
                PendingReview = pendingCount,
                Approved = approvedCount,
                Rejected = rejectedCount,
                InProgress = statusCounts.GetValueOrDefault(IssueStatus.InProgress, 0),
                Resolved = resolvedCount,
                SubmissionsToday = submissionsToday,
                SubmissionsThisWeek = submissionsThisWeek,
                SubmissionsThisMonth = submissionsThisMonth,
                ReviewedToday = reviewedToday,
                ReviewedThisWeek = reviewedThisWeek,
                ReviewedThisMonth = reviewedThisMonth,
                AverageReviewTimeHours = Math.Round(avgReviewTime, 2),
                IssuesByCategory = categoryBreakdown,
                IssuesByUrgency = urgencyBreakdown,
                IssuesByPriority = priorityBreakdown,
                TotalUsers = totalUsers,
                ActiveUsersThisMonth = activeUsersThisMonth,
                TotalEmailsSent = totalEmailsSent,
                ApprovalRate = Math.Round(approvalRate, 2),
                ResolutionRate = Math.Round(resolutionRate, 2),
                BacklogCount = pendingCount,
                Period = period,
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting admin statistics for period: {Period}", period);
            throw;
        }
    }

    public async Task<BulkApproveResponse> BulkApproveIssuesAsync(BulkApproveRequest request, string adminUserId)
    {
        using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        
        try
        {
            BulkApproveResponse response = new()
            {
                TotalRequested = request.IssueIds.Count,
                SuccessfullyApproved = 0,
                Failed = 0,
                Results = []
            };

            // Get admin user
            UserProfile? adminUser = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == adminUserId);

            if (adminUser == null)
            {
                response.Failed = request.IssueIds.Count;
                response.Message = "Admin user not found";
                return response;
            }

            foreach (Guid issueId in request.IssueIds)
            {
                try
                {
                    ApproveIssueRequest approveRequest = new()
                    {
                        AdminNotes = request.AdminNotes,
                        Priority = request.DefaultPriority,
                        AssignedDepartment = request.DefaultDepartment
                    };

                    IssueActionResponse result = await ApproveIssueAsync(issueId, approveRequest, adminUserId);
                    
                    response.Results.Add(new BulkApproveResult
                    {
                        IssueId = issueId,
                        Success = result.Success,
                        Message = result.Message
                    });

                    if (result.Success)
                    {
                        response.SuccessfullyApproved++;
                    }
                    else
                    {
                        response.Failed++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in bulk approve for issue: {IssueId}", issueId);
                    response.Failed++;
                    response.Results.Add(new BulkApproveResult
                    {
                        IssueId = issueId,
                        Success = false,
                        Message = "Error processing issue"
                    });
                }
            }

            await transaction.CommitAsync();

            response.Message = $"Bulk approval completed: {response.SuccessfullyApproved} approved, {response.Failed} failed";
            return response;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error in bulk approve operation");
            throw;
        }
    }

    public async Task<GetModerationStatsResponse> GetModerationStatsAsync(string adminUserId)
    {
        try
        {
            UserProfile? adminUser = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == adminUserId);

            if (adminUser == null)
            {
                throw new InvalidOperationException("Admin user not found");
            }

            List<AdminAction> adminActions = await context.AdminActions
                .Where(aa => aa.AdminUserId == adminUser.Id)
                .ToListAsync();

            DateTime today = DateTime.UtcNow.Date;
            DateTime weekStart = DateTime.UtcNow.AddDays(-(int)DateTime.UtcNow.DayOfWeek).Date;
            DateTime monthStart = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

            // Calculate average review time
            List<Issue> reviewedIssues = await context.Issues
                .Where(i => i.ReviewedBy == adminUser.DisplayName && i.ReviewedAt.HasValue)
                .ToListAsync();

            var avgReviewTime = reviewedIssues.Any()
                ? reviewedIssues.Average(i => (i.ReviewedAt!.Value - i.CreatedAt).TotalHours)
                : 0;

            return new GetModerationStatsResponse
            {
                AdminUserId = adminUser.Id,
                AdminName = adminUser.DisplayName,
                TotalActionsPerformed = adminActions.Count,
                IssuesApproved = adminActions.Count(aa => aa.ActionType == AdminActionType.Approve),
                IssuesRejected = adminActions.Count(aa => aa.ActionType == AdminActionType.Reject),
                ChangesRequested = adminActions.Count(aa => aa.ActionType == AdminActionType.RequestChanges),
                AverageReviewTimeHours = Math.Round(avgReviewTime, 2),
                LastActionDate = adminActions.OrderByDescending(aa => aa.CreatedAt).FirstOrDefault()?.CreatedAt,
                ActionsToday = adminActions.Count(aa => aa.CreatedAt.Date == today),
                ActionsThisWeek = adminActions.Count(aa => aa.CreatedAt >= weekStart),
                ActionsThisMonth = adminActions.Count(aa => aa.CreatedAt >= monthStart)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting moderation stats for admin: {AdminUserId}", adminUserId);
            throw;
        }
    }

    public async Task<PagedResult<AdminActionResponse>> GetAdminActionsAsync(GetAdminActionsRequest request)
    {
        try
        {
            IQueryable<AdminAction> query = context.AdminActions
                .Include(aa => aa.AdminUser)
                .Include(aa => aa.Issue)
                .AsQueryable();

            // Apply filters
            if (request.IssueId.HasValue)
            {
                query = query.Where(aa => aa.IssueId == request.IssueId.Value);
            }

            if (request.AdminUserId.HasValue)
            {
                query = query.Where(aa => aa.AdminUserId == request.AdminUserId.Value);
            }

            if (request.ActionType.HasValue)
            {
                query = query.Where(aa => aa.ActionType == request.ActionType.Value);
            }

            if (request.StartDate.HasValue)
            {
                query = query.Where(aa => aa.CreatedAt >= request.StartDate.Value);
            }

            if (request.EndDate.HasValue)
            {
                query = query.Where(aa => aa.CreatedAt <= request.EndDate.Value);
            }

            // Count before pagination
            var totalCount = await query.CountAsync();

            // Apply sorting
            query = request.SortBy?.ToLower() switch
            {
                "actiontype" => request.SortDescending ? query.OrderByDescending(aa => aa.ActionType) : query.OrderBy(aa => aa.ActionType),
                "adminname" => request.SortDescending ? query.OrderByDescending(aa => aa.AdminUser!.DisplayName) : query.OrderBy(aa => aa.AdminUser!.DisplayName),
                _ => request.SortDescending ? query.OrderByDescending(aa => aa.CreatedAt) : query.OrderBy(aa => aa.CreatedAt)
            };

            // Apply pagination
            List<AdminActionResponse> items = await query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(aa => new AdminActionResponse
                {
                    Id = aa.Id,
                    IssueId = aa.IssueId,
                    IssueTitle = aa.Issue.Title,
                    AdminUserId = aa.AdminUserId,
                    AdminName = aa.AdminUser != null ? aa.AdminUser.DisplayName : "System",
                    AdminEmail = aa.AdminUser != null ? aa.AdminUser.Email : "system@civica.ro",
                    ActionType = aa.ActionType,
                    Notes = aa.Notes,
                    PreviousStatus = aa.PreviousStatus,
                    NewStatus = aa.NewStatus,
                    AssignedDepartment = aa.AssignedDepartment,
                    EstimatedResolutionTime = aa.EstimatedResolutionTime,
                    CreatedAt = aa.CreatedAt
                })
                .ToListAsync();

            return new PagedResult<AdminActionResponse>
            {
                Items = items,
                TotalItems = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting admin actions");
            throw;
        }
    }
}