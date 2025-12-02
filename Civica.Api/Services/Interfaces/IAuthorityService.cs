using Civica.Api.Models.Responses.Authority;
using Civica.Api.Models.Responses.Common;

namespace Civica.Api.Services.Interfaces;

public interface IAuthorityService
{
    /// <summary>
    /// Get all active predefined authorities
    /// </summary>
    Task<List<AuthorityListResponse>> GetActiveAuthoritiesAsync();

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
    Task<AuthorityResponse> CreateAuthorityAsync(string name, string email);

    /// <summary>
    /// Update an authority (admin only)
    /// </summary>
    Task<AuthorityResponse?> UpdateAuthorityAsync(Guid id, string name, string email);

    /// <summary>
    /// Deactivate an authority (admin only)
    /// </summary>
    Task<bool> DeactivateAuthorityAsync(Guid id);
}
