using Civica.Api.Models.Responses.Moderation;

namespace Civica.Api.Services.Interfaces;

public interface IContentModerationService
{
    Task<ContentModerationResponse> ModerateContentAsync(string content);
}
