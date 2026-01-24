namespace Civica.Api.Infrastructure.Configuration;

public class OpenAIConfiguration
{
    public const string DefaultModerationModel = "omni-moderation-latest";
    public const int DefaultTimeoutSeconds = 10;

    public string ApiKey { get; set; } = string.Empty;
    public string ModerationModel { get; set; } = DefaultModerationModel;
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
