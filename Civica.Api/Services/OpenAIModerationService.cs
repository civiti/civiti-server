using System.ClientModel;
using Civica.Api.Infrastructure.Configuration;
using Civica.Api.Models.Responses.Moderation;
using Civica.Api.Services.Interfaces;
using OpenAI.Moderations;

namespace Civica.Api.Services;

public class OpenAIModerationService(
    ILogger<OpenAIModerationService> logger,
    OpenAIConfiguration configuration) : IContentModerationService
{
    // Categories that result in content being blocked
    private static readonly HashSet<string> BlockedCategories =
    [
        "hate",
        "hate/threatening",
        "harassment",           // blocks general insults/vulgarity
        "harassment/threatening",
        "violence/graphic",
        "self-harm/instructions",
        "sexual",               // blocks sexual content
        "sexual/minors"
    ];

    public async Task<ContentModerationResponse> ModerateContentAsync(string content)
    {
        // If not configured, allow all content (graceful degradation)
        if (!configuration.IsConfigured)
        {
            logger.LogDebug("OpenAI moderation not configured, skipping moderation check");
            return new ContentModerationResponse { IsAllowed = true };
        }

        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(configuration.TimeoutSeconds));

            ModerationClient client = new(model: configuration.ModerationModel, apiKey: configuration.ApiKey);
            ClientResult<ModerationResult>? result = await client.ClassifyTextAsync(content, cts.Token);

            if (result?.Value == null)
            {
                logger.LogWarning("OpenAI moderation returned null result, allowing content (fail-open)");
                return new ContentModerationResponse { IsAllowed = true };
            }

            ModerationResult moderationResult = result.Value;
            List<string> flaggedBlockedCategories = [];

            // Check each blocked category
            if (moderationResult.Hate.Flagged && BlockedCategories.Contains("hate"))
                flaggedBlockedCategories.Add("hate");

            if (moderationResult.HateThreatening.Flagged && BlockedCategories.Contains("hate/threatening"))
                flaggedBlockedCategories.Add("hate/threatening");

            if (moderationResult.Harassment.Flagged && BlockedCategories.Contains("harassment"))
                flaggedBlockedCategories.Add("harassment");

            if (moderationResult.HarassmentThreatening.Flagged && BlockedCategories.Contains("harassment/threatening"))
                flaggedBlockedCategories.Add("harassment/threatening");

            if (moderationResult.ViolenceGraphic.Flagged && BlockedCategories.Contains("violence/graphic"))
                flaggedBlockedCategories.Add("violence/graphic");

            if (moderationResult.SelfHarmInstructions.Flagged && BlockedCategories.Contains("self-harm/instructions"))
                flaggedBlockedCategories.Add("self-harm/instructions");

            if (moderationResult.Sexual.Flagged && BlockedCategories.Contains("sexual"))
                flaggedBlockedCategories.Add("sexual");

            if (moderationResult.SexualMinors.Flagged && BlockedCategories.Contains("sexual/minors"))
                flaggedBlockedCategories.Add("sexual/minors");

            if (flaggedBlockedCategories.Count > 0)
            {
                logger.LogInformation(
                    "Content blocked by moderation. Categories: {Categories}",
                    string.Join(", ", flaggedBlockedCategories));

                return new ContentModerationResponse
                {
                    IsAllowed = false,
                    BlockReason = "Content violates community guidelines",
                    BlockedCategories = flaggedBlockedCategories
                };
            }

            // Log if content was flagged but in allowed categories (for monitoring)
            if (moderationResult.Flagged)
            {
                List<string> allowedFlaggedCategories = [];

                if (moderationResult.Violence.Flagged) allowedFlaggedCategories.Add("violence");
                if (moderationResult.SelfHarm.Flagged) allowedFlaggedCategories.Add("self-harm");

                if (allowedFlaggedCategories.Count > 0)
                {
                    logger.LogDebug(
                        "Content flagged in allowed categories: {Categories}",
                        string.Join(", ", allowedFlaggedCategories));
                }
            }

            return new ContentModerationResponse { IsAllowed = true };
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("OpenAI moderation request timed out, allowing content (fail-open)");
            return new ContentModerationResponse { IsAllowed = true };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenAI moderation request failed, allowing content (fail-open)");
            return new ContentModerationResponse { IsAllowed = true };
        }
    }
}
