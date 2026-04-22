namespace Civiti.Domain.Entities;

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
    /// Romanian county (Județ) where this authority operates (e.g., "București", "Cluj")
    /// </summary>
    public string County { get; set; } = string.Empty;

    /// <summary>
    /// City where this authority operates (e.g., "București", "Cluj-Napoca")
    /// </summary>
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// District/Sector within city (e.g., "Sector 1"). Null for city-wide authorities.
    /// </summary>
    public string? District { get; set; }

    /// <summary>
    /// Whether this authority is currently active and available for selection
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public List<IssueAuthority> IssueAuthorities { get; set; } = [];
}
