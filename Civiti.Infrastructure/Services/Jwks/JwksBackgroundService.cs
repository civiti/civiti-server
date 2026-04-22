using Civiti.Infrastructure.Configuration;
using Civiti.Application.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Civiti.Infrastructure.Services.Jwks;

/// <summary>
/// Background service that continuously refreshes JWKS to ensure keys are always available
/// This eliminates the need for synchronous async calls in the IssuerSigningKeyResolver
/// </summary>
public class JwksBackgroundService : BackgroundService
{
    private readonly IJwksManager _jwksManager;
    private readonly JwtValidationOptions _options;
    private readonly ILogger<JwksBackgroundService> _logger;
    private readonly TimeSpan _refreshInterval;

    public JwksBackgroundService(
        IJwksManager jwksManager,
        IOptions<JwtValidationOptions> options,
        ILogger<JwksBackgroundService> logger)
    {
        _jwksManager = jwksManager ?? throw new ArgumentNullException(nameof(jwksManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Refresh at half the cache TTL to ensure fresh keys
        _refreshInterval = TimeSpan.FromMilliseconds(_options.JwksCacheTtlMs / 2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JWKS background service starting - refresh interval: {Interval}",
            _refreshInterval);

        // Initial load with retry
        await LoadJwksWithRetryAsync(stoppingToken);

        // Continuous refresh loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    await LoadJwksWithRetryAsync(stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when service is stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in JWKS background service");
                // Continue running despite errors
            }
        }

        _logger.LogInformation("JWKS background service stopping");
    }

    private async Task LoadJwksWithRetryAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var attempt = 0;

        while (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                attempt++;
                _logger.LogDebug("Loading JWKS (attempt {Attempt}/{MaxAttempts})", attempt, maxAttempts);

                JsonWebKeySet jwks = await _jwksManager.GetJwksAsync(forceRefresh: true, cancellationToken);

                _logger.LogInformation("JWKS refreshed successfully with {KeyCount} keys",
                    jwks.Keys.Count);

                // Log available key IDs for debugging
                var kids = string.Join(", ", jwks.Keys.Select(k => k.Kid ?? "null"));
                _logger.LogDebug("Available key IDs: {Kids}", kids);

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh JWKS on attempt {Attempt}/{MaxAttempts}",
                    attempt, maxAttempts);

                if (attempt < maxAttempts)
                {
                    TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        _logger.LogError("Failed to refresh JWKS after {MaxAttempts} attempts", maxAttempts);
    }
}