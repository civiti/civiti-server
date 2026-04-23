using Civiti.Application.Responses.Authority;

namespace Civiti.Application.Services;

public interface IAuthorityService
{
    /// <summary>
    /// Get all active predefined authorities with optional filtering
    /// </summary>
    /// <param name="city">Filter by city (e.g., "București")</param>
    /// <param name="district">Filter by district within city (e.g., "Sector 1")</param>
    /// <param name="search">Search by authority name</param>
    Task<List<AuthorityListResponse>> GetActiveAuthoritiesAsync(
        string? city = null,
        string? district = null,
        string? search = null);

    /// <summary>
    /// Get authority by ID
    /// </summary>
    Task<AuthorityResponse?> GetAuthorityByIdAsync(Guid id);

    /// <summary>
    /// Get authorities linked to an issue
    /// </summary>
    Task<List<IssueAuthorityResponse>> GetAuthoritiesForIssueAsync(Guid issueId);

    // Admin operations

    /// <summary>
    /// Create a new predefined authority (admin only)
    /// </summary>
    Task<AuthorityResponse> CreateAuthorityAsync(
        string name,
        string email,
        string county,
        string city,
        string? district);

    /// <summary>
    /// Update an authority (admin only)
    /// </summary>
    Task<AuthorityResponse?> UpdateAuthorityAsync(
        Guid id,
        string name,
        string email,
        string county,
        string city,
        string? district);

    /// <summary>
    /// Deactivate an authority (admin only)
    /// </summary>
    Task<bool> DeactivateAuthorityAsync(Guid id);
}
