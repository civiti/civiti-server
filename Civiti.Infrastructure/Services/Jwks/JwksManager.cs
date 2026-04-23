using System.Text.Json;
using Civiti.Infrastructure.Configuration;
using Civiti.Application.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Civiti.Infrastructure.Services.Jwks;

/// <summary>
/// Service for managing JWKS (JSON Web Key Set) operations
/// Handles fetching, caching, and key resolution for JWT validation with Supabase
/// </summary>
public class JwksManager : IJwksManager
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly JwtValidationOptions _options;
    private readonly ILogger<JwksManager> _logger;

    // Cache keys
    private const string JwksCacheKey = "jwks_keys";
    private const string StatsCacheKey = "jwks_stats";

    // Cache statistics
    private int _totalRequests;
    private int _cacheHits;
    private DateTime? _lastRefresh;

    /// <summary>
    /// Initializes a new instance of the JwksManager
    /// </summary>
    public JwksManager(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<JwtValidationOptions> options,
        ILogger<JwksManager> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Validate configuration
        if (string.IsNullOrWhiteSpace(_options.JwksUrl))
        {
            throw new ArgumentException("JwksUrl is required in JwtValidationOptions", nameof(options));
        }

        if (!Uri.TryCreate(_options.JwksUrl, UriKind.Absolute, out Uri? jwksUri) ||
            (jwksUri.Scheme != Uri.UriSchemeHttp && jwksUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"JwksUrl must be a valid absolute HTTP/HTTPS URL: {_options.JwksUrl}", nameof(options));
        }

        // Load cache statistics
        if (_cache.TryGetValue(StatsCacheKey, out var stats) && stats is CacheStats cachedStats)
        {
            _totalRequests = cachedStats.TotalRequests;
            _cacheHits = cachedStats.CacheHits;
            _lastRefresh = cachedStats.LastRefresh;
        }

        _logger.LogInformation("JwksManager initialized with JWKS URL: {JwksUrl}", _options.JwksUrl);
    }

    /// <inheritdoc />
    public async Task<JsonWebKeySet> GetJwksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalRequests);
        UpdateCacheStats();

        // Try to get from cache first (unless force refresh)
        if (!forceRefresh && _cache.TryGetValue(JwksCacheKey, out JsonWebKeySet? cached) && cached != null)
        {
            Interlocked.Increment(ref _cacheHits);
            UpdateCacheStats();
            _logger.LogDebug("JWKS retrieved from cache");
            return cached;
        }

        _logger.LogInformation("Fetching JWKS from {JwksUrl} (forceRefresh: {ForceRefresh})", _options.JwksUrl, forceRefresh);

        // Fetch from remote endpoint with retry logic
        JsonWebKeySet jwks = await FetchJwksWithRetryAsync(cancellationToken);

        // Cache the result
        MemoryCacheEntryOptions cacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(_options.JwksCacheTtlMs),
            Priority = CacheItemPriority.High,
            Size = 1 // JWKS is relatively small
        };

        _cache.Set(JwksCacheKey, jwks, cacheOptions);
        _lastRefresh = DateTime.UtcNow;
        UpdateCacheStats();

        _logger.LogInformation("JWKS cached successfully with {KeyCount} keys", jwks.Keys.Count);
        return jwks;
    }

    /// <inheritdoc />
    public async Task<JsonWebKey?> GetKeyForKidAsync(string kid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(kid))
        {
            _logger.LogWarning("GetKeyForKidAsync called with null or empty kid");
            return null;
        }

        // First attempt: get from cache
        JsonWebKeySet jwks = await GetJwksAsync(false, cancellationToken);
        JsonWebKey? key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);

        if (key != null)
        {
            _logger.LogDebug("Key found for kid: {Kid}", kid);
            return key;
        }

        // Key not found - try refreshing once (handles key rotation)
        _logger.LogInformation("Key not found for kid: {Kid}, refreshing JWKS", kid);
        jwks = await GetJwksAsync(true, cancellationToken);
        key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);

        if (key != null)
        {
            _logger.LogInformation("Key found for kid: {Kid} after refresh", kid);
        }
        else
        {
            _logger.LogWarning("Key not found for kid: {Kid} even after refresh. Available kids: {AvailableKids}",
                kid, string.Join(", ", jwks.Keys.Select(k => k.Kid ?? "null")));
        }

        return key;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default)
    {
        JsonWebKeySet jwks = await GetJwksAsync(false, cancellationToken);

        // Filter to only signing keys (exclude encryption keys)
        List<SecurityKey> signingKeys = jwks.Keys
            .Where(k => k.Use == null || k.Use == "sig") // null use means both signing and encryption
            .Where(k => k.KeyOps == null || k.KeyOps.Contains("verify")) // null keyOps means all operations
            .Cast<SecurityKey>()
            .ToList();

        _logger.LogDebug("Retrieved {Count} signing keys from JWKS", signingKeys.Count);
        return signingKeys;
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cache.Remove(JwksCacheKey);
        _logger.LogInformation("JWKS cache cleared");
    }

    /// <inheritdoc />
    public (double HitRate, DateTime? LastRefresh, int TotalRequests) GetCacheStats()
    {
        var hitRate = _totalRequests > 0 ? (double)_cacheHits / _totalRequests : 0.0;
        return (hitRate, _lastRefresh, _totalRequests);
    }

    /// <summary>
    /// Fetches JWKS from remote endpoint with retry logic
    /// </summary>
    private async Task<JsonWebKeySet> FetchJwksWithRetryAsync(CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient("JwksClient");
        client.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        Exception? lastException = null;

        for (int attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug("JWKS fetch attempt {Attempt}/{MaxRetries}", attempt, _options.MaxRetries);

                using HttpResponseMessage response = await client.GetAsync(_options.JwksUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidOperationException("JWKS response was empty");
                    }

                    try
                    {
                        // Validate JSON structure before creating JsonWebKeySet
                        using JsonDocument jsonDoc = JsonDocument.Parse(content);
                        if (!jsonDoc.RootElement.TryGetProperty("keys", out JsonElement keysElement) ||
                            keysElement.ValueKind != JsonValueKind.Array)
                        {
                            throw new InvalidOperationException("JWKS response does not contain a valid 'keys' array");
                        }

                        JsonWebKeySet jwks = new(content);

                        if (jwks.Keys.Count == 0)
                        {
                            throw new InvalidOperationException("JWKS contains no keys");
                        }

                        _logger.LogInformation("Successfully fetched JWKS with {KeyCount} keys on attempt {Attempt}",
                            jwks.Keys.Count, attempt);
                        return jwks;
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidOperationException($"Invalid JSON in JWKS response: {ex.Message}", ex);
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new HttpRequestException(
                        $"JWKS fetch failed with status {response.StatusCode}: {errorContent}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                lastException = ex;
                _logger.LogWarning(ex, "JWKS fetch attempt {Attempt} failed: {Message}", attempt, ex.Message);

                // If this wasn't the last attempt, wait before retrying
                if (attempt < _options.MaxRetries)
                {
                    TimeSpan delay = TimeSpan.FromMilliseconds(_options.RetryDelayMs * attempt); // Linear backoff
                    _logger.LogDebug("Waiting {Delay}ms before retry attempt {NextAttempt}",
                        delay.TotalMilliseconds, attempt + 1);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        // If we have a cached value, return it as fallback
        if (_cache.TryGetValue(JwksCacheKey, out JsonWebKeySet? fallback) && fallback != null)
        {
            _logger.LogWarning("Using stale cached JWKS as fallback after fetch failures");
            return fallback;
        }

        // No cached fallback available
        var errorMessage = $"Failed to fetch JWKS after {_options.MaxRetries} attempts";
        _logger.LogError(lastException, errorMessage);
        throw new InvalidOperationException(errorMessage, lastException);
    }

    /// <summary>
    /// Updates cache statistics
    /// </summary>
    private void UpdateCacheStats()
    {
        CacheStats stats = new(_totalRequests, _cacheHits, _lastRefresh);
        _cache.Set(StatsCacheKey, stats, TimeSpan.FromHours(24)); // Keep stats for 24 hours
    }

    /// <summary>
    /// Internal record for tracking cache statistics
    /// </summary>
    private record CacheStats(int TotalRequests, int CacheHits, DateTime? LastRefresh);

    /// <inheritdoc />
    public JsonWebKeySet? GetCachedJwks()
    {
        // Synchronous cache lookup only - no async operations
        if (_cache.TryGetValue(JwksCacheKey, out JsonWebKeySet? cached) && cached != null)
        {
            _logger.LogDebug("Retrieved JWKS from cache (synchronous)");
            return cached;
        }

        _logger.LogDebug("No JWKS found in cache (synchronous)");
        return null;
    }

    /// <inheritdoc />
    public IEnumerable<SecurityKey> GetCachedSigningKeys()
    {
        // Synchronous cache lookup only - no async operations
        JsonWebKeySet? cachedJwks = GetCachedJwks();

        if (cachedJwks != null)
        {
            // Filter to only signing keys (exclude encryption keys)
            List<SecurityKey> signingKeys = cachedJwks.Keys
                .Where(k => k.Use == null || k.Use == "sig") // null use means both signing and encryption
                .Where(k => k.KeyOps == null || k.KeyOps.Contains("verify")) // null keyOps means all operations
                .Cast<SecurityKey>()
                .ToList();

            _logger.LogDebug("Retrieved {Count} cached signing keys (synchronous)", signingKeys.Count);
            return signingKeys;
        }

        _logger.LogDebug("No cached signing keys available (synchronous)");
        return [];
    }
}