using Civiti.Api.Models.Requests.Issues;
using Civiti.Api.Models.Responses.Common;
using Civiti.Api.Models.Responses.Issues;

namespace Civiti.Api.Services.Interfaces;

public interface IIssueService
{
    Task<PagedResult<IssueListResponse>> GetAllIssuesAsync(GetIssuesRequest request, Guid? currentUserId = null);
    Task<IssueDetailResponse?> GetIssueByIdAsync(Guid id, Guid? currentUserId = null);
    Task<CreateIssueResponse> CreateIssueAsync(CreateIssueRequest request, string supabaseUserId);
    Task<(bool Success, string? Error)> IncrementEmailCountAsync(Guid issueId, string? clientIp);
    Task<PagedResult<IssueListResponse>> GetUserIssuesAsync(string supabaseUserId, GetUserIssuesRequest request);

    /// <summary>
    /// Update an issue's status (user can only change status of their own issues, unless admin).
    /// Allowed user transitions: Cancelled, Resolved
    /// </summary>
    Task<(bool Success, string? Error)> UpdateIssueStatusAsync(Guid issueId, UpdateIssueStatusRequest request, string supabaseUserId, bool isAdmin = false);

    /// <summary>
    /// Update an issue (user can only edit their own issues, except Cancelled/Resolved).
    /// After update, status is set to UnderReview.
    /// </summary>
    Task<(bool Success, IssueDetailResponse? Issue, string? Error)> UpdateIssueAsync(
        Guid issueId,
        UpdateIssueRequest request,
        string supabaseUserId);

    /// <summary>
    /// Vote for an issue to show community support.
    /// Awards points to the issue author.
    /// </summary>
    Task<(bool Success, string? Error)> VoteForIssueAsync(Guid issueId, string supabaseUserId);

    /// <summary>
    /// Remove a vote from an issue.
    /// Deducts points from the issue author.
    /// </summary>
    Task<(bool Success, string? Error)> RemoveVoteAsync(Guid issueId, string supabaseUserId);
}