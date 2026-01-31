using System.ComponentModel.DataAnnotations;

namespace Civiti.Api.Models.Requests.Comments;

public class GetCommentsRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100")]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Sort by: "date" (default) or "helpful"
    /// </summary>
    public string SortBy { get; set; } = "date";

    /// <summary>
    /// Sort in descending order (default: true for date, true for helpful)
    /// </summary>
    public bool SortDescending { get; set; } = true;
}
