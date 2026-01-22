using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Activity;

public class GetActivitiesRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public ActivityType? Type { get; set; }
    public DateTime? Since { get; set; }
}
