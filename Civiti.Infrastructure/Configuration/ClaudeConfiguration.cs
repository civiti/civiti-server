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
    /// improvements roll in. Set &lt;= 0 to disable petition caching entirely (no read or write) — the
    /// content-hash invalidation only applies while caching is enabled.</summary>
    public const int DefaultPetitionCacheHours = 24;

    // Upper bound so a misconfigured (very large) PetitionCacheHours can't overflow TimeSpan.FromHours
    // and 500 every request; 10 years is effectively "cache forever" here. Larger values clamp, not throw.
    private const int MaxPetitionCacheHours = 87_600;

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = DefaultModel;
    public int MaxTokens { get; set; } = DefaultMaxTokens;
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
    public int RateLimitPerMinute { get; set; } = DefaultRateLimitPerMinute;
    public int PetitionCacheHours { get; set; } = DefaultPetitionCacheHours;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Petition-core cache lifetime as a <see cref="TimeSpan"/>; <see cref="TimeSpan.Zero"/> when
    /// disabled (<see cref="PetitionCacheHours"/> &lt;= 0). Clamped to <see cref="MaxPetitionCacheHours"/> so a
    /// large misconfiguration pins the cache instead of overflowing.</summary>
    public TimeSpan PetitionCacheTtl =>
        PetitionCacheHours <= 0 ? TimeSpan.Zero : TimeSpan.FromHours(Math.Min(PetitionCacheHours, MaxPetitionCacheHours));
}
