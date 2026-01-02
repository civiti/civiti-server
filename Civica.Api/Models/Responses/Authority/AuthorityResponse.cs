namespace Civica.Api.Models.Responses.Authority;

/// <summary>
/// Response model for authority details
/// </summary>
public class AuthorityResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Response model for authority in list views
/// </summary>
public class AuthorityListResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
}

/// <summary>
/// Response model for authority linked to an issue
/// </summary>
public class IssueAuthorityResponse
{
    /// <summary>
    /// ID of the predefined authority (null if custom)
    /// </summary>
    public Guid? AuthorityId { get; set; }

    /// <summary>
    /// Authority name (from predefined or custom)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Authority email (from predefined or custom)
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// True if this is a predefined authority, false if custom
    /// </summary>
    public bool IsPredefined { get; set; }
}
