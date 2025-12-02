namespace Civica.Api.Models.Domain;

/// <summary>
/// Represents a predefined government authority that can be contacted about civic issues
/// </summary>
public class Authority
{
    public Guid Id { get; set; }

    /// <summary>
    /// Official name of the authority
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Official contact email for the authority
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Whether this authority is currently active and available for selection
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public List<IssueAuthority> IssueAuthorities { get; set; } = [];
}
