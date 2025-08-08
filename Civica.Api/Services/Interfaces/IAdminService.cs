using Civica.Api.Models.Requests.Admin;
using Civica.Api.Models.Responses.Admin;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Services.Interfaces;

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