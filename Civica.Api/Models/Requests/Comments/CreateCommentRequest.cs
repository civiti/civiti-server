using System.ComponentModel.DataAnnotations;

namespace Civica.Api.Models.Requests.Comments;

public class CreateCommentRequest
{
    [Required(ErrorMessage = "Content is required")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Content must be between 1 and 2000 characters")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional parent comment ID for replies. Null for top-level comments.
    /// </summary>
    public Guid? ParentCommentId { get; set; }
}
