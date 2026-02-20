using System.ComponentModel.DataAnnotations;

namespace Civiti.Api.Models.Requests.Admin;

/// <summary>
/// Request to ask the issue author for changes before approval
/// </summary>
public class RequestChangesRequest
{
    /// <summary>
    /// Description of the changes needed, shown to the issue author
    /// </summary>
    /// <example>Please add a more specific address and upload at least one photo of the issue.</example>
    [Required]
    [MaxLength(2000)]
    public string RequestedChanges { get; set; } = string.Empty;
}
