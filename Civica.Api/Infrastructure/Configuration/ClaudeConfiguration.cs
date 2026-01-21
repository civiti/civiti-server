namespace Civica.Api.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for Claude AI integration
/// </summary>
public class ClaudeConfiguration
{
    public const string DefaultModel = "claude-sonnet-4-5-20250929";
    public const int DefaultMaxTokens = 2048;
    public const int DefaultTimeoutSeconds = 30;
    public const int DefaultRateLimitPerMinute = 10;

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = DefaultModel;
    public int MaxTokens { get; set; } = DefaultMaxTokens;
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
    public int RateLimitPerMinute { get; set; } = DefaultRateLimitPerMinute;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
