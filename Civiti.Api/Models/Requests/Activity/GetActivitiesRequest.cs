using System.ComponentModel.DataAnnotations;
using Civiti.Api.Models.Domain;

namespace Civiti.Api.Models.Requests.Activity;

public class GetActivitiesRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100")]
    public int PageSize { get; set; } = 20;

    public ActivityType? Type { get; set; }
    public DateTime? Since { get; set; }
}
