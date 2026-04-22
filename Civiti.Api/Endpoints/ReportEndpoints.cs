using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Domain.Exceptions;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Models.Requests.Reports;
using Civiti.Api.Models.Responses.Reports;
using Civiti.Api.Services.Interfaces;

namespace Civiti.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        // Issue report group
        RouteGroupBuilder issueGroup = app.MapGroup(ApiRoutes.Issues.Base)
            .WithTags("Reports")
            .RequireAuthorization();

        // POST /api/issues/{id}/report
        issueGroup.MapPost(ApiRoutes.Reports.Report, async (
            Guid id,
            CreateReportRequest request,
            HttpContext context,
            IReportService reportService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var (success, reportId, error) = await reportService.ReportIssueAsync(id, request, supabaseUserId);

                if (!success)
                {
                    if (error == DomainErrors.ReportRateLimited)
                        context.Response.Headers.RetryAfter = "3600";

                    return error switch
                    {
                        DomainErrors.IssueNotFound => Results.NotFound(new { error }),
                        DomainErrors.IssueNotReportable => Results.BadRequest(new { error }),
                        DomainErrors.AlreadyReported => Results.Conflict(new { error }),
                        DomainErrors.ReportRateLimited => Results.Json(new { error }, statusCode: StatusCodes.Status429TooManyRequests),
                        DomainErrors.CannotReportOwnContent => Results.BadRequest(new { error }),
                        _ => Results.BadRequest(new { error })
                    };
                }

                return Results.Json(new ReportResponse
                {
                    Id = reportId!.Value,
                    Message = "Report submitted successfully"
                }, statusCode: StatusCodes.Status201Created);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("ReportIssue")
        .WithSummary("Report an issue")
        .WithDescription("Reports an issue for moderation. Each user can only report a given issue once. Rate limited to 5 reports per hour.")
        .Produces<ReportResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status429TooManyRequests);

        // Comment report group
        RouteGroupBuilder commentGroup = app.MapGroup(ApiRoutes.Comments.Base)
            .WithTags("Reports")
            .RequireAuthorization();

        // POST /api/comments/{id}/report
        commentGroup.MapPost(ApiRoutes.Reports.Report, async (
            Guid id,
            CreateReportRequest request,
            HttpContext context,
            IReportService reportService) =>
        {
            var supabaseUserId = context.User.GetSupabaseUserId();
            if (string.IsNullOrEmpty(supabaseUserId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var (success, reportId, error) = await reportService.ReportCommentAsync(id, request, supabaseUserId);

                if (!success)
                {
                    if (error == DomainErrors.ReportRateLimited)
                        context.Response.Headers.RetryAfter = "3600";

                    return error switch
                    {
                        DomainErrors.CommentNotFound => Results.NotFound(new { error }),
                        DomainErrors.CommentNotReportable => Results.BadRequest(new { error }),
                        DomainErrors.AlreadyReported => Results.Conflict(new { error }),
                        DomainErrors.ReportRateLimited => Results.Json(new { error }, statusCode: StatusCodes.Status429TooManyRequests),
                        DomainErrors.CannotReportOwnContent => Results.BadRequest(new { error }),
                        _ => Results.BadRequest(new { error })
                    };
                }

                return Results.Json(new ReportResponse
                {
                    Id = reportId!.Value,
                    Message = "Report submitted successfully"
                }, statusCode: StatusCodes.Status201Created);
            }
            catch (AccountDeletedException)
            {
                return Results.Problem(
                    detail: DomainErrors.AccountDeleted,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account Deleted");
            }
        })
        .WithName("ReportComment")
        .WithSummary("Report a comment")
        .WithDescription("Reports a comment for moderation. Each user can only report a given comment once. Rate limited to 5 reports per hour.")
        .Produces<ReportResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status429TooManyRequests);
    }
}
