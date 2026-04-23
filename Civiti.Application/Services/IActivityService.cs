using Civiti.Domain.Entities;
using Civiti.Application.Requests.Activity;
using Civiti.Application.Responses.Activity;
using Civiti.Application.Responses.Common;

namespace Civiti.Application.Services;

public interface IActivityService
{
    Task<PagedResult<ActivityResponse>> GetUserActivitiesAsync(Guid userId, GetActivitiesRequest request);
    Task<PagedResult<ActivityResponse>> GetRecentActivitiesAsync(GetActivitiesRequest request);
    Task RecordActivityAsync(ActivityType type, Guid issueId, Guid? actorUserId = null, string? metadata = null);
    Task RecordSupporterActivityAsync(Guid issueId);
}
