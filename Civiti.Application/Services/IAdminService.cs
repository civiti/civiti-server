using Civiti.Application.Requests.Admin;
using Civiti.Application.Responses.Admin;
using Civiti.Application.Responses.Common;

namespace Civiti.Application.Services;

public interface IAdminService
{
    Task<PagedResult<AdminIssueResponse>> GetPendingIssuesAsync(GetPendingIssuesRequest request);
    Task<AdminIssueDetailResponse?> GetIssueDetailsForAdminAsync(Guid issueId);
    Task<IssueActionResponse> ApproveIssueAsync(Guid issueId, ApproveIssueRequest request, string adminUserId);
    Task<IssueActionResponse> RejectIssueAsync(Guid issueId, RejectIssueRequest request, string adminUserId);
    Task<IssueActionResponse> RequestChangesAsync(Guid issueId, RequestChangesRequest request, string adminUserId);
    Task<AdminStatisticsResponse> GetStatisticsAsync(string period = "30d");
    Task<BulkApproveResponse> BulkApproveIssuesAsync(BulkApproveRequest request, string adminUserId);
    Task<GetModerationStatsResponse> GetModerationStatsAsync(string adminUserId);
    Task<PagedResult<AdminActionResponse>> GetAdminActionsAsync(GetAdminActionsRequest request);
}