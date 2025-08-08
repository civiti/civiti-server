using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Services.Interfaces;

public interface IIssueService
{
    Task<PagedResult<IssueListResponse>> GetAllIssuesAsync(GetIssuesRequest request);
    Task<IssueDetailResponse?> GetIssueByIdAsync(Guid id);
    Task<CreateIssueResponse> CreateIssueAsync(CreateIssueRequest request, string supabaseUserId);
    Task<bool> TrackEmailSentAsync(Guid issueId, TrackEmailRequest request, string supabaseUserId);
    Task<PagedResult<IssueListResponse>> GetUserIssuesAsync(string supabaseUserId, GetUserIssuesRequest request);
}