using Civiti.Domain.Attributes;

namespace Civiti.Application.Responses.Authority;

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
    /// Authority name (from predefined or custom). Tagged <see cref="UntrustedAttribute"/>
    /// because the population path collapses both into the same field — when
    /// <see cref="IsPredefined"/> is false, this comes from <c>IssueAuthority.CustomName</c>,
    /// which is free-text supplied by the issue author. The MCP serializer wraps it in the
    /// quarantine envelope; the (slight) cost on the predefined branch is wrapping a
    /// well-known public-record string, which is still safe to surface.
    /// </summary>
    [Untrusted] public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Authority email (from predefined or custom). Tagged for the same reason as
    /// <see cref="Name"/> — when <see cref="IsPredefined"/> is false this is
    /// <c>IssueAuthority.CustomEmail</c>, which is user-supplied.
    /// </summary>
    [Untrusted] public string Email { get; set; } = string.Empty;

    /// <summary>
    /// True if this is a predefined authority, false if custom
    /// </summary>
    public bool IsPredefined { get; set; }
}
