using System.Threading.RateLimiting;
using Anthropic.SDK;
using Civiti.Application.Services;
using Civiti.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Civiti.Infrastructure.Services.Claude;

/// <summary>
/// The one registration of the Claude stack, shared by Civiti.Api and Civiti.Mcp so the two
/// services can't drift apart in how they read settings or which model they call.
/// </summary>
public static class ClaudeServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="ClaudeConfiguration"/> (env var first, then appsettings, then the code
    /// default) and registers the shared <see cref="AnthropicClient"/>, the per-user rate limiter,
    /// the petition cache store and <see cref="IClaudeEnhancementService"/>. Returns the bound
    /// configuration so the host can log it. The model is not read from configuration at all; see
    /// <see cref="ClaudeConfiguration.Model"/>.
    /// </summary>
    public static ClaudeConfiguration AddClaudeEnhancement(this IServiceCollection services, IConfiguration configuration)
    {
        ClaudeConfiguration claudeConfig = new()
        {
            ApiKey = GetString(configuration, "CLAUDE_API_KEY", "Claude:ApiKey") ?? string.Empty,
            MaxTokens = GetInt(configuration, "CLAUDE_MAX_TOKENS", "Claude:MaxTokens", ClaudeConfiguration.DefaultMaxTokens),
            TimeoutSeconds = GetInt(configuration, "CLAUDE_TIMEOUT_SECONDS", "Claude:TimeoutSeconds", ClaudeConfiguration.DefaultTimeoutSeconds),
            RateLimitPerMinute = GetInt(configuration, "CLAUDE_RATE_LIMIT_PER_MINUTE", "Claude:RateLimitPerMinute", ClaudeConfiguration.DefaultRateLimitPerMinute),
            PetitionCacheHours = GetInt(configuration, "PETITION_CACHE_HOURS", "Claude:PetitionCacheHours", ClaudeConfiguration.DefaultPetitionCacheHours),
            IgnoredModelOverride = GetString(configuration, "CLAUDE_MODEL", "Claude:Model")
        };
        services.AddSingleton(claudeConfig);

        // AnthropicClient wraps a SocketsHttpHandler-backed HttpClient. Register as a singleton so
        // the connection pool is shared across all ClaudeEnhancementService calls — constructing a
        // new client per request exhausts sockets under load. When Claude is not configured the
        // client is still registered but never exercised: ClaudeEnhancementService short-circuits
        // on ClaudeConfiguration.IsConfigured before calling it.
        services.AddSingleton(_ => new AnthropicClient(claudeConfig.ApiKey));

        // Per-user sliding window shared by both AI features.
        services.AddSingleton(_ => PartitionedRateLimiter.Create<Guid, Guid>(userId =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: userId,
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = claudeConfig.RateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6, // 10-second segments for smoother rate limiting
                    QueueLimit = 0,
                    AutoReplenishment = true
                })));

        services.AddScoped<IPetitionBodyCacheStore, PetitionBodyCacheStore>();
        services.AddScoped<IClaudeEnhancementService, ClaudeEnhancementService>();

        return claudeConfig;
    }

    // Same precedence as Civiti.Api's GetEnvOrConfig helpers: a non-blank env var wins, else the
    // appsettings key, else the default.
    private static string? GetString(IConfiguration configuration, string envVar, string configKey)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = configuration[configKey];
        }
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int GetInt(IConfiguration configuration, string envVar, string configKey, int defaultValue) =>
        int.TryParse(Environment.GetEnvironmentVariable(envVar), out var result)
            ? result
            : configuration.GetValue(configKey, defaultValue);
}
