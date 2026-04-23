using Civiti.Application.Responses.Moderation;

namespace Civiti.Application.Services;

public interface IContentModerationService
{
    Task<ContentModerationResponse> ModerateContentAsync(string content);
}
