using System.Security.Claims;
using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Admin;
using Civiti.Api.Models.Responses.Admin;
using Civiti.Api.Models.Responses.Common;
using Civiti.Api.Services.Interfaces;

namespace Civiti.Api.Endpoints;

/// <summary>
/// Administrative endpoints for issue moderation and system management
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// Maps admin-related endpoints to the application
    /// </summary>
    /// <param name="app">The web application to map endpoints to</param>
    /// <remarks>
    /// All endpoints in this group require admin authentication and authorization.
    /// Admin role is validated through JWT claims.
    /// </remarks>
    public static void MapAdminEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(ApiRoutes.Admin.Base)
            .WithTags("Admin")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        // GET /api/admin/pending-issues
        group.MapGet(ApiRoutes.Admin.PendingIssues, async (
            IAdminService adminService,
            int page = 1,
            int pageSize = 20,
            string? category = null,
            string? urgency = null,
            string? searchTerm = null,
            DateTime? submittedAfter = null,
            DateTime? submittedBefore = null,
            string sortBy = "CreatedAt",
            bool sortDescending = true) =>
        {
            // Validate pagination parameters
            var actualPage = Math.Max(page, 1);
            var actualPageSize = Math.Clamp(pageSize, 1, 100);

            GetPendingIssuesRequest request = new()
            {
                Page = actualPage,
                PageSize = actualPageSize,
                SearchTerm = searchTerm,
                SubmittedAfter = submittedAfter,
                SubmittedBefore = submittedBefore,
                SortBy = sortBy,
                SortDescending = sortDescending
            };

            if (!string.IsNullOrEmpty(category) && Enum.TryParse(category, out IssueCategory categoryEnum))
            {
                request.Category = categoryEnum;
            }

            if (!string.IsNullOrEmpty(urgency) && Enum.TryParse(urgency, out UrgencyLevel urgencyEnum))
            {
                request.Urgency = urgencyEnum;
            }

            PagedResult<AdminIssueResponse> result = await adminService.GetPendingIssuesAsync(request);
            return Results.Ok(result);
        })
        .WithName("GetPendingIssues")
        .WithSummary("Get issues pending admin review")
        .WithDescription("Retrieves a paginated list of issues awaiting admin approval. Supports advanced filtering by category, urgency, date range, and search terms. This endpoint is critical for the moderation workflow and only accessible to administrators.")
        .Produces<PagedResult<AdminIssueResponse>>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .WithOpenApi(operation =>
        {
            operation.Parameters[0].Description = "Page number (default: 1)";
            operation.Parameters[1].Description = "Items per page (default: 20, max: 100)";
            operation.Parameters[2].Description = "Filter by issue category";
            operation.Parameters[3].Description = "Filter by urgency level";
            operation.Parameters[4].Description = "Search in title and description";
            operation.Parameters[5].Description = "Filter issues submitted after this date";
            operation.Parameters[6].Description = "Filter issues submitted before this date";
            operation.Parameters[7].Description = "Sort field (CreatedAt, Urgency, Category)";
            operation.Parameters[8].Description = "Sort in descending order (default: true)";
            return operation;
        });

        // GET /api/admin/issues/{id}
        group.MapGet(ApiRoutes.Admin.IssueById, async (
            Guid id,
            IAdminService adminService) =>
        {
            AdminIssueDetailResponse? issue = await adminService.GetIssueDetailsForAdminAsync(id);
            return issue == null 
                ? Results.NotFound(new { message = "Issue not found" })
                : Results.Ok(issue);
        })
        .WithName("GetIssueDetailsForAdmin")
        .WithSummary("Get detailed issue information for admin review")
        .WithDescription("Retrieves comprehensive issue details including user information, full content, photos, and moderation history. This provides all information needed for admins to make informed moderation decisions.")
        .Produces<AdminIssueDetailResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi();

        // PUT /api/admin/issues/{id}/approve
        group.MapPut(ApiRoutes.Admin.Approve, async (
            Guid id,
            ApproveIssueRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.GetSupabaseUserId()
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            IssueActionResponse result = await adminService.ApproveIssueAsync(id, request, adminUserId);
            return result.Success 
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("ApproveIssue")
        .WithSummary("Approve an issue")
        .Produces<IssueActionResponse>()
        .Produces<IssueActionResponse>(StatusCodes.Status400BadRequest);

        // PUT /api/admin/issues/{id}/reject
        group.MapPut(ApiRoutes.Admin.Reject, async (
            Guid id,
            RejectIssueRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.GetSupabaseUserId()
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            IssueActionResponse result = await adminService.RejectIssueAsync(id, request, adminUserId);
            return result.Success 
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("RejectIssue")
        .WithSummary("Reject an issue with reason")
        .Produces<IssueActionResponse>()
        .Produces<IssueActionResponse>(StatusCodes.Status400BadRequest);

        // PUT /api/admin/issues/{id}/request-changes
        group.MapPut(ApiRoutes.Admin.RequestChanges, async (
            Guid id,
            RequestChangesRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.GetSupabaseUserId()
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            IssueActionResponse result = await adminService.RequestChangesAsync(id, request, adminUserId);
            return result.Success 
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("RequestChanges")
        .WithSummary("Request changes on an issue")
        .Produces<IssueActionResponse>()
        .Produces<IssueActionResponse>(StatusCodes.Status400BadRequest);

        // GET /api/admin/statistics
        group.MapGet(ApiRoutes.Admin.Statistics, async (
            IAdminService adminService,
            string period = "30d") =>
        {
            AdminStatisticsResponse stats = await adminService.GetStatisticsAsync(period);
            return Results.Ok(stats);
        })
        .WithName("GetAdminStatistics")
        .WithSummary("Get admin dashboard statistics")
        .Produces<AdminStatisticsResponse>();

        // POST /api/admin/bulk-approve
        group.MapPost(ApiRoutes.Admin.BulkApprove, async (
            BulkApproveRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.GetSupabaseUserId()
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            BulkApproveResponse result = await adminService.BulkApproveIssuesAsync(request, adminUserId);
            return Results.Ok(result);
        })
        .WithName("BulkApproveIssues")
        .WithSummary("Bulk approve multiple issues")
        .Produces<BulkApproveResponse>();

        // GET /api/admin/moderation-stats
        group.MapGet(ApiRoutes.Admin.ModerationStats, async (
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.GetSupabaseUserId()
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            GetModerationStatsResponse stats = await adminService.GetModerationStatsAsync(adminUserId);
            return Results.Ok(stats);
        })
        .WithName("GetModerationStats")
        .WithSummary("Get moderation statistics for the current admin")
        .Produces<GetModerationStatsResponse>();

        // GET /api/admin/actions
        group.MapGet(ApiRoutes.Admin.Actions, async (
            IAdminService adminService,
            int page = 1,
            int pageSize = 50,
            Guid? issueId = null,
            Guid? adminUserId = null,
            string? actionType = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string sortBy = "CreatedAt",
            bool sortDescending = true) =>
        {
            // Validate pagination parameters
            var actualPage = Math.Max(page, 1);
            var actualPageSize = Math.Clamp(pageSize, 1, 100);

            GetAdminActionsRequest request = new()
            {
                Page = actualPage,
                PageSize = actualPageSize,
                IssueId = issueId,
                AdminUserId = adminUserId,
                StartDate = startDate,
                EndDate = endDate,
                SortBy = sortBy,
                SortDescending = sortDescending
            };

            if (!string.IsNullOrEmpty(actionType) && Enum.TryParse(actionType, out AdminActionType actionTypeEnum))
            {
                request.ActionType = actionTypeEnum;
            }

            PagedResult<AdminActionResponse> result = await adminService.GetAdminActionsAsync(request);
            return Results.Ok(result);
        })
        .WithName("GetAdminActions")
        .WithSummary("Get admin action audit log")
        .Produces<PagedResult<AdminActionResponse>>();
    }
}