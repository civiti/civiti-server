namespace Civiti.Api.Models.Domain;

/// <summary>
/// Join table linking issues to their target authorities.
/// Supports both predefined authorities (via AuthorityId) and custom user-entered authorities (via CustomName/CustomEmail).
/// </summary>
public class IssueAuthority
{
    public Guid Id { get; set; }

    public Guid IssueId { get; set; }

    /// <summary>
    /// Reference to a predefined authority. Null if this is a custom authority.
    /// </summary>
    public Guid? AuthorityId { get; set; }

    /// <summary>
    /// Custom authority name. Only used when AuthorityId is null.
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    /// Custom authority email. Only used when AuthorityId is null.
    /// </summary>
    public string? CustomEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Issue Issue { get; set; } = null!;
    public Authority? Authority { get; set; }
}
