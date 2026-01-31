using Civiti.Api.Models.Responses.Moderation;

namespace Civiti.Api.Services.Interfaces;

public interface IContentModerationService
{
    Task<ContentModerationResponse> ModerateContentAsync(string content);
}
