namespace Civiti.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for Claude AI integration
/// </summary>
public class ClaudeConfiguration
{
    public const string DefaultModel = "claude-haiku-4-5";
    public const int DefaultMaxTokens = 2048;
    public const int DefaultTimeoutSeconds = 30;
    public const int DefaultRateLimitPerMinute = 10;

    /// <summary>How long a cached petition core stays fresh before it's re-composed. The core is
    /// user-agnostic and only changes when the issue's prompt-affecting fields do (handled by a
    /// content hash), so a long window is safe; the TTL just bounds staleness and lets prompt/model
    /// improvements roll in. Set <= 0 to disable time-based caching (content-hash caching stays).</summary>
    public const int DefaultPetitionCacheHours = 24;

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = DefaultModel;
    public int MaxTokens { get; set; } = DefaultMaxTokens;
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
    public int RateLimitPerMinute { get; set; } = DefaultRateLimitPerMinute;
    public int PetitionCacheHours { get; set; } = DefaultPetitionCacheHours;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Petition-core cache lifetime as a <see cref="TimeSpan"/>; <see cref="TimeSpan.Zero"/> when disabled.</summary>
    public TimeSpan PetitionCacheTtl => PetitionCacheHours > 0 ? TimeSpan.FromHours(PetitionCacheHours) : TimeSpan.Zero;
}
