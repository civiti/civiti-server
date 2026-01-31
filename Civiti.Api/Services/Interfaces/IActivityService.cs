using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Activity;
using Civiti.Api.Models.Responses.Activity;
using Civiti.Api.Models.Responses.Common;

namespace Civiti.Api.Services.Interfaces;

public interface IActivityService
{
    Task<PagedResult<ActivityResponse>> GetUserActivitiesAsync(Guid userId, GetActivitiesRequest request);
    Task<PagedResult<ActivityResponse>> GetRecentActivitiesAsync(GetActivitiesRequest request);
    Task RecordActivityAsync(ActivityType type, Guid issueId, Guid? actorUserId = null, string? metadata = null);
    Task RecordSupporterActivityAsync(Guid issueId);
}
