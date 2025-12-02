using Civica.Api.Models.Responses.Issues;
using Civica.Api.Models.Responses.Common;
using Civica.Api.Models.Requests.Issues;

namespace Civica.Api.Services.Interfaces;

public interface IIssueService
{
    Task<PagedResult<IssueListResponse>> GetAllIssuesAsync(GetIssuesRequest request);
    Task<IssueDetailResponse?> GetIssueByIdAsync(Guid id);
    Task<CreateIssueResponse> CreateIssueAsync(CreateIssueRequest request, string supabaseUserId);
    Task<(bool Success, string? Error)> IncrementEmailCountAsync(Guid issueId, string? clientIp);
    Task<PagedResult<IssueListResponse>> GetUserIssuesAsync(string supabaseUserId, GetUserIssuesRequest request);
}