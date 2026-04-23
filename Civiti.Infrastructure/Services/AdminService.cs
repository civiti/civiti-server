using Civiti.Infrastructure.Data;
using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Civiti.Application.Requests.Admin;
using Civiti.Application.Responses.Admin;
using Civiti.Application.Responses.Authority;
using Civiti.Application.Responses.Common;
using Civiti.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Civiti.Infrastructure.Services;

public class AdminService(
    ILogger<AdminService> logger,
    CivitiDbContext context,
    IGamificationService gamificationService,
    IActivityService activityService,
    INotificationService notificationService)
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
                    Status = i.Status,
                    Address = i.Address,
                    CreatedAt = i.CreatedAt,
                    PhotoCount = i.Photos.Count,
                    EmailsSent = i.EmailsSent,
                    UserName = i.User != null ? i.User.DisplayName : "Deleted User"
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
            // IgnoreQueryFilters disables the UserProfile global filter so that
            // Include(i => i.User) loads deleted authors (showing their anonymized PII)
            // instead of silently returning null.
            Issue? issue = await context.Issues
                .IgnoreQueryFilters()
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

            // Execute count queries before building response to avoid N+1 queries
            var userTotalIssues = await context.Issues.CountAsync(i => i.UserId == issue.UserId);
            var userResolvedIssues = await context.Issues
                .CountAsync(i => i.UserId == issue.UserId && i.Status == IssueStatus.Resolved);

            return new AdminIssueDetailResponse
            {
                Id = issue.Id,
                Title = issue.Title,
                Description = issue.Description,
                Category = issue.Category,
                Urgency = issue.Urgency,
                Status = issue.Status,
                Address = issue.Address,
                Latitude = issue.Latitude,
                Longitude = issue.Longitude,
                District = issue.District,
                DesiredOutcome = issue.DesiredOutcome,
                CommunityImpact = issue.CommunityImpact,
                AdminNotes = issue.AdminNotes,
                RejectionReason = issue.RejectionReason,
                ReviewedAt = issue.ReviewedAt,
                ReviewedBy = issue.ReviewedBy,
                CreatedAt = issue.CreatedAt,
                UpdatedAt = issue.UpdatedAt,
                UserId = issue.UserId,
                UserName = issue.User?.DisplayName ?? "Deleted User",
                UserEmail = issue.User?.Email ?? string.Empty,
                UserPhone = issue.User?.Phone,
                UserTotalIssues = userTotalIssues,
                UserResolvedIssues = userResolvedIssues,
                UserPoints = issue.User?.Points ?? 0,
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
        // Use execution strategy to handle transient failures with proper transaction support
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Clear change tracker to ensure fresh data on retry
            context.ChangeTracker.Clear();

            Issue? issue = await context.Issues
                .Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = DomainErrors.IssueNotFound
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

            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Update issue
                var previousStatus = issue.Status.ToString();
                issue.Status = IssueStatus.Active;
                issue.ReviewedAt = DateTime.UtcNow;
                issue.ReviewedBy = adminUser.DisplayName;
                issue.AdminNotes = request.AdminNotes;
                issue.UpdatedAt = DateTime.UtcNow;

                // Create admin action record
                context.AdminActions.Add(new AdminAction
                {
                    Id = Guid.NewGuid(),
                    IssueId = issueId,
                    AdminUserId = adminUser.Id,
                    AdminSupabaseId = adminUserId,
                    ActionType = AdminActionType.Approve,
                    Notes = request.AdminNotes,
                    PreviousStatus = previousStatus,
                    NewStatus = IssueStatus.Active.ToString(),
                    CreatedAt = DateTime.UtcNow
                });

                // Award points and badges (don't save yet - accumulate all changes)
                await gamificationService.AwardPointsAsync(issue.UserId, 50, $"Issue approved: {issue.Title}", saveChanges: false);
                await gamificationService.CheckAndAwardBadgesAsync(issue.UserId, saveChanges: false);

                // Single atomic save for all changes (issue, admin action, points, badges)
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Flush gamification notifications now that the transaction is committed
                await gamificationService.FlushPendingNotificationsAsync();

                // Record activity (outside transaction to avoid issues)
                try
                {
                    await activityService.RecordActivityAsync(
                        ActivityType.IssueApproved,
                        issueId,
                        adminUser.Id);
                }
                catch (Exception activityEx)
                {
                    logger.LogError(activityEx, "Failed to record IssueApproved activity for issue {IssueId}", issueId);
                }

                // Send notification to issue author
                try
                {
                    UserProfile? issueAuthor = await context.UserProfiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == issue.UserId);
                    if (issueAuthor != null)
                    {
                        await notificationService.NotifyIssueApprovedAsync(issue, issueAuthor);
                    }
                }
                catch (Exception notifyEx)
                {
                    logger.LogError(notifyEx, "Failed to send approval notification for issue {IssueId}", issueId);
                }

                logger.LogInformation(
                    "Issue approved: {IssueId} by admin: {AdminUserId}",
                    issueId,
                    adminUserId);

                return new IssueActionResponse
                {
                    Success = true,
                    Message = "Issue approved successfully",
                    IssueId = issueId,
                    NewStatus = IssueStatus.Active.ToString(),
                    UpdatedAt = DateTime.UtcNow
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<IssueActionResponse> RejectIssueAsync(Guid issueId, RejectIssueRequest request, string adminUserId)
    {
        // Use execution strategy to handle transient failures with proper transaction support
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Clear change tracker to ensure fresh data on retry
            context.ChangeTracker.Clear();

            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = DomainErrors.IssueNotFound
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

            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Update issue
                var previousStatus = issue.Status.ToString();
                issue.Status = IssueStatus.Rejected;
                issue.ReviewedAt = DateTime.UtcNow;
                issue.ReviewedBy = adminUser.DisplayName;
                issue.RejectionReason = request.Reason;
                issue.AdminNotes = request.InternalNotes;
                issue.UpdatedAt = DateTime.UtcNow;

                // Create admin action record
                context.AdminActions.Add(new AdminAction
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
                });

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Send notification to issue author
                try
                {
                    UserProfile? issueAuthor = await context.UserProfiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == issue.UserId);
                    if (issueAuthor != null)
                    {
                        await notificationService.NotifyIssueRejectedAsync(issue, issueAuthor, request.Reason);
                    }
                }
                catch (Exception notifyEx)
                {
                    logger.LogError(notifyEx, "Failed to send rejection notification for issue {IssueId}", issueId);
                }

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
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<IssueActionResponse> RequestChangesAsync(Guid issueId, RequestChangesRequest request, string adminUserId)
    {
        // Use execution strategy to handle transient failures with proper transaction support
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Clear change tracker to ensure fresh data on retry
            context.ChangeTracker.Clear();

            Issue? issue = await context.Issues
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue == null)
            {
                return new IssueActionResponse
                {
                    Success = false,
                    Message = DomainErrors.IssueNotFound
                };
            }

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

            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Update issue
                var previousStatus = issue.Status.ToString();
                issue.Status = IssueStatus.UnderReview;
                issue.AdminNotes = request.RequestedChanges;
                issue.UpdatedAt = DateTime.UtcNow;

                // Create admin action record
                context.AdminActions.Add(new AdminAction
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
                });

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Send notification to issue author
                try
                {
                    UserProfile? issueAuthor = await context.UserProfiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == issue.UserId);
                    if (issueAuthor != null)
                    {
                        await notificationService.NotifyChangesRequestedAsync(issue, issueAuthor, request.RequestedChanges);
                    }
                }
                catch (Exception notifyEx)
                {
                    logger.LogError(notifyEx, "Failed to send changes-requested notification for issue {IssueId}", issueId);
                }

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
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
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
                _ => DateTime.MinValue.ToUniversalTime()
            };

            // PostgreSQL requires DateTimeKind.Utc for timestamp with time zone
            DateTime today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
            DateTime weekStart = DateTime.SpecifyKind(now.AddDays(-(int)now.DayOfWeek).Date, DateTimeKind.Utc);
            DateTime monthStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1), DateTimeKind.Utc);

            // Build base query with period filter - all aggregations happen in the database
            IQueryable<Issue> periodQuery = startDate != DateTime.MinValue.ToUniversalTime()
                ? context.Issues.Where(i => i.CreatedAt >= startDate)
                : context.Issues;

            // Count by status using database aggregation
            var statusCountsRaw = await periodQuery
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            Dictionary<IssueStatus, int> statusCounts = statusCountsRaw.ToDictionary(x => x.Status, x => x.Count);

            // Count submissions by time using database queries
            var submissionsToday = await context.Issues.CountAsync(i => i.CreatedAt >= today);
            var submissionsThisWeek = await context.Issues.CountAsync(i => i.CreatedAt >= weekStart);
            var submissionsThisMonth = await context.Issues.CountAsync(i => i.CreatedAt >= monthStart);

            // Admin activity counts using database aggregation
            var reviewedToday = await context.AdminActions.CountAsync(aa => aa.CreatedAt >= today);
            var reviewedThisWeek = await context.AdminActions.CountAsync(aa => aa.CreatedAt >= weekStart);
            var reviewedThisMonth = await context.AdminActions.CountAsync(aa => aa.CreatedAt >= monthStart);

            // Calculate average review time using database projection
            var reviewTimeData = await context.Issues
                .Where(i => i.ReviewedAt.HasValue && (startDate == DateTime.MinValue.ToUniversalTime() || i.CreatedAt >= startDate))
                .Select(i => new { ReviewedAt = i.ReviewedAt!.Value, i.CreatedAt })
                .ToListAsync();

            var avgReviewTime = reviewTimeData.Count != 0
                ? reviewTimeData.Average(i => (i.ReviewedAt - i.CreatedAt).TotalHours)
                : 0;

            // Category breakdown using database aggregation
            var categoryCountsRaw = await periodQuery
                .GroupBy(i => i.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync();
            Dictionary<string, int> categoryBreakdown = categoryCountsRaw.ToDictionary(x => x.Category.ToString(), x => x.Count);

            // Urgency breakdown using database aggregation
            var urgencyCountsRaw = await periodQuery
                .GroupBy(i => i.Urgency)
                .Select(g => new { Urgency = g.Key, Count = g.Count() })
                .ToListAsync();
            Dictionary<string, int> urgencyBreakdown = urgencyCountsRaw.ToDictionary(x => x.Urgency.ToString(), x => x.Count);

            // User statistics (include soft-deleted users so the dashboard reflects true totals)
            var totalUsers = await context.UserProfiles.IgnoreQueryFilters().CountAsync();
            var activeUsersThisMonth = await context.Issues
                .Where(i => i.CreatedAt >= monthStart)
                .Select(i => i.UserId)
                .Distinct()
                .CountAsync();

            var totalEmailsSent = await context.Issues.SumAsync(i => i.EmailsSent);

            // Performance metrics
            var totalIssues = await periodQuery.CountAsync();
            var activeCount = statusCounts.GetValueOrDefault(IssueStatus.Active, 0);
            var rejectedCount = statusCounts.GetValueOrDefault(IssueStatus.Rejected, 0);
            var resolvedCount = statusCounts.GetValueOrDefault(IssueStatus.Resolved, 0);
            var pendingCount = statusCounts.GetValueOrDefault(IssueStatus.Submitted, 0) +
                              statusCounts.GetValueOrDefault(IssueStatus.UnderReview, 0);

            var approvalRate = (activeCount + rejectedCount) > 0
                ? (double)activeCount / (activeCount + rejectedCount) * 100
                : 0;

            var resolutionRate = activeCount > 0
                ? (double)resolvedCount / activeCount * 100
                : 0;

            return new AdminStatisticsResponse
            {
                TotalSubmissions = totalIssues,
                PendingReview = pendingCount,
                Approved = activeCount, // Active status is now used for approved issues
                Rejected = rejectedCount,
                Active = activeCount,
                Resolved = resolvedCount,
                Cancelled = statusCounts.GetValueOrDefault(IssueStatus.Cancelled, 0),
                SubmissionsToday = submissionsToday,
                SubmissionsThisWeek = submissionsThisWeek,
                SubmissionsThisMonth = submissionsThisMonth,
                ReviewedToday = reviewedToday,
                ReviewedThisWeek = reviewedThisWeek,
                ReviewedThisMonth = reviewedThisMonth,
                AverageReviewTimeHours = Math.Round(avgReviewTime, 2),
                IssuesByCategory = categoryBreakdown,
                IssuesByUrgency = urgencyBreakdown,
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

    private const int MaxBulkApproveLimit = 50;

    public async Task<BulkApproveResponse> BulkApproveIssuesAsync(BulkApproveRequest request, string adminUserId)
    {
        // Validate bulk operation size to prevent timeouts and resource exhaustion
        if (request.IssueIds.Count > MaxBulkApproveLimit)
        {
            return new BulkApproveResponse
            {
                TotalRequested = request.IssueIds.Count,
                SuccessfullyApproved = 0,
                Failed = request.IssueIds.Count,
                Message = $"Cannot approve more than {MaxBulkApproveLimit} issues at once. Please reduce the batch size.",
                Results = []
            };
        }

        BulkApproveResponse response = new()
        {
            TotalRequested = request.IssueIds.Count,
            SuccessfullyApproved = 0,
            Failed = 0,
            Results = []
        };

        foreach (Guid issueId in request.IssueIds)
        {
            ApproveIssueRequest approveRequest = new()
            {
                AdminNotes = request.AdminNotes
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
                // Clear change tracker to prevent polluting subsequent approvals
                // with any partial changes from the failed approval
                context.ChangeTracker.Clear();
            }
        }

        response.Message = $"Bulk approval completed: {response.SuccessfullyApproved} approved, {response.Failed} failed";
        return response;
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

            // PostgreSQL requires DateTimeKind.Utc for timestamp with time zone
            DateTime now = DateTime.UtcNow;
            DateTime today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
            DateTime weekStart = DateTime.SpecifyKind(now.AddDays(-(int)now.DayOfWeek).Date, DateTimeKind.Utc);
            DateTime monthStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1), DateTimeKind.Utc);

            // Calculate average review time. `Issue.ReviewedBy` is a display-name snapshot
            // that goes stale when an admin renames themselves, so we can't filter by it
            // safely. Instead, walk the AdminActions we already loaded (filtered by the
            // stable AdminUserId FK) and look up the matching Issues — an admin's reviews
            // are exactly the set of Issues that have an Approve or Reject AdminAction
            // authored by them.
            HashSet<Guid> reviewedIssueIds = adminActions
                .Where(aa => aa.ActionType is AdminActionType.Approve or AdminActionType.Reject)
                .Select(aa => aa.IssueId)
                .ToHashSet();

            List<Issue> reviewedIssues = reviewedIssueIds.Count == 0
                ? []
                : await context.Issues
                    .Where(i => reviewedIssueIds.Contains(i.Id) && i.ReviewedAt.HasValue)
                    .ToListAsync();

            var avgReviewTime = reviewedIssues.Count > 0
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
