using Anthropic.SDK.Messaging;

namespace Civiti.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for Claude AI integration
/// </summary>
public class ClaudeConfiguration
{
    /// <summary>
    /// The Claude model behind every AI feature (issue text enhancement and petition drafting, in
    /// both Civiti-Server and Civiti-MCP). <b>This constant is the one place to change it.</b> It is
    /// deliberately not overridable from env/appsettings: a model change usually needs request
    /// changes too (effort, thinking, max_tokens, stop reasons), so an env var that swaps the model
    /// without the code is how the two drift apart. It also feeds the petition-cache hash, so
    /// changing it invalidates every cached petition core. Before changing it, follow
    /// docs/technical/ai-models.md.
    /// </summary>
    public const string Model = "claude-sonnet-5-5";

    /// <summary>
    /// output_config.effort sent with every request. Both features are short, single-turn rewriting
    /// tasks, which is what <c>low</c> is for; Sonnet 5.5's default is <c>high</c>, which would add
    /// thinking (and latency) to every call. Thinking itself is left at the model's default
    /// (adaptive) — at <c>low</c> the model skips it on most simple requests.
    /// </summary>
    public const ThinkingEffort Effort = ThinkingEffort.low;

    public const int DefaultMaxTokens = 4096;
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

    /// <summary>Upper bound on output tokens per request. Thinking counts toward it, so leave headroom
    /// above the longest expected reply.</summary>
    public int MaxTokens { get; set; } = DefaultMaxTokens;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
    public int RateLimitPerMinute { get; set; } = DefaultRateLimitPerMinute;
    public int PetitionCacheHours { get; set; } = DefaultPetitionCacheHours;

    /// <summary>The value of a legacy <c>CLAUDE_MODEL</c> / <c>Claude:Model</c> setting found at startup,
    /// which is now ignored (see <see cref="Model"/>). Surfaced only so startup can warn that the
    /// variable should be deleted; null when none is set.</summary>
    public string? IgnoredModelOverride { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Petition-core cache lifetime as a <see cref="TimeSpan"/>; <see cref="TimeSpan.Zero"/> when
    /// disabled (<see cref="PetitionCacheHours"/> &lt;= 0). Clamped to <see cref="MaxPetitionCacheHours"/> so a
    /// large misconfiguration pins the cache instead of overflowing.</summary>
    public TimeSpan PetitionCacheTtl =>
        PetitionCacheHours <= 0 ? TimeSpan.Zero : TimeSpan.FromHours(Math.Min(PetitionCacheHours, MaxPetitionCacheHours));
}
