using Microsoft.AspNetCore.Authorization;
using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Models.Requests.Admin;
using Civica.Api.Models.Responses.Admin;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Models.Domain;
using System.Security.Claims;

namespace Civica.Api.Endpoints;

public static class AdminEndpoints
{
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
            GetPendingIssuesRequest request = new GetPendingIssuesRequest
            {
                Page = page,
                PageSize = pageSize,
                SearchTerm = searchTerm,
                SubmittedAfter = submittedAfter,
                SubmittedBefore = submittedBefore,
                SortBy = sortBy,
                SortDescending = sortDescending
            };

            if (!string.IsNullOrEmpty(category) && Enum.TryParse<IssueCategory>(category, out IssueCategory categoryEnum))
            {
                request.Category = categoryEnum;
            }

            if (!string.IsNullOrEmpty(urgency) && Enum.TryParse<UrgencyLevel>(urgency, out UrgencyLevel urgencyEnum))
            {
                request.Urgency = urgencyEnum;
            }

            PagedResult<AdminIssueResponse> result = await adminService.GetPendingIssuesAsync(request);
            return Results.Ok(result);
        })
        .WithName("GetPendingIssues")
        .WithSummary("Get issues pending admin review")
        .Produces<PagedResult<AdminIssueResponse>>(StatusCodes.Status200OK);

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
        .Produces<AdminIssueDetailResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // PUT /api/admin/issues/{id}/approve
        group.MapPut(ApiRoutes.Admin.Approve, async (
            Guid id,
            ApproveIssueRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.FindFirst(AuthorizationPolicies.Claims.UserId)?.Value
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            IssueActionResponse result = await adminService.ApproveIssueAsync(id, request, adminUserId);
            return result.Success 
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("ApproveIssue")
        .WithSummary("Approve an issue")
        .Produces<IssueActionResponse>(StatusCodes.Status200OK)
        .Produces<IssueActionResponse>(StatusCodes.Status400BadRequest);

        // PUT /api/admin/issues/{id}/reject
        group.MapPut(ApiRoutes.Admin.Reject, async (
            Guid id,
            RejectIssueRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.FindFirst(AuthorizationPolicies.Claims.UserId)?.Value
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            IssueActionResponse result = await adminService.RejectIssueAsync(id, request, adminUserId);
            return result.Success 
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("RejectIssue")
        .WithSummary("Reject an issue with reason")
        .Produces<IssueActionResponse>(StatusCodes.Status200OK)
        .Produces<IssueActionResponse>(StatusCodes.Status400BadRequest);

        // PUT /api/admin/issues/{id}/request-changes
        group.MapPut(ApiRoutes.Admin.RequestChanges, async (
            Guid id,
            RequestChangesRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.FindFirst(AuthorizationPolicies.Claims.UserId)?.Value
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            IssueActionResponse result = await adminService.RequestChangesAsync(id, request, adminUserId);
            return result.Success 
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("RequestChanges")
        .WithSummary("Request changes on an issue")
        .Produces<IssueActionResponse>(StatusCodes.Status200OK)
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
        .Produces<AdminStatisticsResponse>(StatusCodes.Status200OK);

        // POST /api/admin/bulk-approve
        group.MapPost(ApiRoutes.Admin.BulkApprove, async (
            BulkApproveRequest request,
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.FindFirst(AuthorizationPolicies.Claims.UserId)?.Value
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            BulkApproveResponse result = await adminService.BulkApproveIssuesAsync(request, adminUserId);
            return Results.Ok(result);
        })
        .WithName("BulkApproveIssues")
        .WithSummary("Bulk approve multiple issues")
        .Produces<BulkApproveResponse>(StatusCodes.Status200OK);

        // GET /api/admin/moderation-stats
        group.MapGet("/moderation-stats", async (
            IAdminService adminService,
            ClaimsPrincipal user) =>
        {
            var adminUserId = user.FindFirst(AuthorizationPolicies.Claims.UserId)?.Value
                ?? throw new UnauthorizedAccessException("Admin user ID not found");

            GetModerationStatsResponse stats = await adminService.GetModerationStatsAsync(adminUserId);
            return Results.Ok(stats);
        })
        .WithName("GetModerationStats")
        .WithSummary("Get moderation statistics for the current admin")
        .Produces<GetModerationStatsResponse>(StatusCodes.Status200OK);

        // GET /api/admin/actions
        group.MapGet("/actions", async (
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
            GetAdminActionsRequest request = new GetAdminActionsRequest
            {
                Page = page,
                PageSize = pageSize,
                IssueId = issueId,
                AdminUserId = adminUserId,
                StartDate = startDate,
                EndDate = endDate,
                SortBy = sortBy,
                SortDescending = sortDescending
            };

            if (!string.IsNullOrEmpty(actionType) && Enum.TryParse<AdminActionType>(actionType, out AdminActionType actionTypeEnum))
            {
                request.ActionType = actionTypeEnum;
            }

            PagedResult<AdminActionResponse> result = await adminService.GetAdminActionsAsync(request);
            return Results.Ok(result);
        })
        .WithName("GetAdminActions")
        .WithSummary("Get admin action audit log")
        .Produces<PagedResult<AdminActionResponse>>(StatusCodes.Status200OK);
    }
}