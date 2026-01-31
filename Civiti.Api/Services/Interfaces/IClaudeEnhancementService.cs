using Civiti.Api.Models.Requests.Issues;
using Civiti.Api.Models.Responses.Issues;

namespace Civiti.Api.Services.Interfaces;

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
    /// Checks if a user is rate limited
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <returns>True if the user is rate limited</returns>
    bool IsRateLimited(Guid userId);
}
