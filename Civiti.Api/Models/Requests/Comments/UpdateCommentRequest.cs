using System.ComponentModel.DataAnnotations;

namespace Civiti.Api.Models.Requests.Comments;

public class UpdateCommentRequest
{
    [Required(ErrorMessage = "Content is required")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Content must be between 1 and 2000 characters")]
    public string Content { get; set; } = string.Empty;
}
