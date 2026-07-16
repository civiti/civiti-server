using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Issues;

namespace Civiti.Application.Services;

/// <summary>
/// Service for enhancing civic issue text using Claude AI
/// </summary>
public interface IClaudeEnhancementService
{
    /// <summary>
    /// Enhances the provided text using Claude AI
    /// </summary>
    /// <param name="request">The text enhancement request</param>
    /// <param name="userId">The user ID for rate limiting</param>
    /// <returns>Enhanced text response</returns>
    Task<EnhanceTextResponse> EnhanceTextAsync(EnhanceTextRequest request, Guid userId);

    /// <summary>
    /// Generates a full, ready-to-copy Romanian petition body for an issue. The model
    /// composes only the argument core (problem → impact → demand); the legally-required
    /// O.G. 27/2002 scaffold and PII placeholders are concatenated deterministically, so
    /// compliance is guaranteed regardless of model output. On any AI failure a deterministic
    /// core (built from the raw issue fields) is used — the body is never empty.
    /// </summary>
    /// <param name="request">The petition-body generation input, assembled from a stored issue</param>
    /// <param name="userId">The user ID for rate limiting</param>
    /// <returns>The composed petition body</returns>
    Task<PetitionBodyResponse> GeneratePetitionBodyAsync(PetitionBodyRequest request, Guid userId);

    /// <summary>
    /// Checks if a user is rate limited
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <returns>True if the user is rate limited</returns>
    bool IsRateLimited(Guid userId);
}
