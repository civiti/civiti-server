using Civiti.Application.Requests.Reports;

namespace Civiti.Application.Services;

public interface IReportService
{
    Task<(bool Success, Guid? ReportId, string? Error)> ReportIssueAsync(Guid issueId, CreateReportRequest request, string supabaseUserId);
    Task<(bool Success, Guid? ReportId, string? Error)> ReportCommentAsync(Guid commentId, CreateReportRequest request, string supabaseUserId);
}
