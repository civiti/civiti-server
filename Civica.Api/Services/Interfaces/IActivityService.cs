using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Activity;
using Civica.Api.Models.Responses.Activity;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Services.Interfaces;

public interface IActivityService
{
    Task<PagedResult<ActivityResponse>> GetUserActivitiesAsync(Guid userId, GetActivitiesRequest request);
    Task<PagedResult<ActivityResponse>> GetRecentActivitiesAsync(GetActivitiesRequest request);
    Task RecordActivityAsync(ActivityType type, Guid issueId, Guid? actorUserId = null, string? metadata = null);
    Task RecordSupporterActivityAsync(Guid issueId);
}
