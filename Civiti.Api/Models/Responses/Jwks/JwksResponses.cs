namespace Civiti.Api.Models.Responses.Jwks;

/// <summary>
/// JWKS cache statistics nested in health and stats responses
/// </summary>
public class JwksCacheInfo
{
    /// <summary>
    /// Cache hit rate as a percentage (0-100)
    /// </summary>
    /// <example>95.5</example>
    public double HitRate { get; set; }

    /// <summary>
    /// ISO 8601 string of the last cache refresh time
    /// </summary>
    /// <example>2026-02-20 14:30:00 UTC</example>
    public string LastRefresh { get; set; } = "Never";

    /// <summary>
    /// Total number of JWKS resolution requests
    /// </summary>
    /// <example>1250</example>
    public int TotalRequests { get; set; }
}

/// <summary>
/// Response for the JWKS health check endpoint
/// </summary>
public class JwksHealthResponse
{
    /// <summary>
    /// Health status: Healthy or Unhealthy
    /// </summary>
    /// <example>Healthy</example>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// JWKS endpoint connectivity status
    /// </summary>
    /// <example>Connected</example>
    public string JwksEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Number of signing keys available
    /// </summary>
    /// <example>1</example>
    public int KeyCount { get; set; }

    /// <summary>
    /// Cache performance statistics
    /// </summary>
    public JwksCacheInfo Cache { get; set; } = new();

    /// <summary>
    /// UTC timestamp of the health check
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Available key IDs for JWT validation
    /// </summary>
    public string[] AvailableKeyIds { get; set; } = [];
}

/// <summary>
/// Detailed key information for the stats endpoint
/// </summary>
public class JwksKeyDetail
{
    /// <summary>
    /// Key ID
    /// </summary>
    public string? Kid { get; set; }

    /// <summary>
    /// Key type (e.g. RSA)
    /// </summary>
    public string? Kty { get; set; }

    /// <summary>
    /// Key usage (e.g. sig)
    /// </summary>
    public string? Use { get; set; }

    /// <summary>
    /// Algorithm (e.g. RS256)
    /// </summary>
    public string? Alg { get; set; }

    /// <summary>
    /// Key operations
    /// </summary>
    public string[]? KeyOps { get; set; }
}

/// <summary>
/// Extended cache statistics for the stats endpoint
/// </summary>
public class JwksCacheStats
{
    /// <summary>
    /// Cache hit rate as a percentage
    /// </summary>
    public double HitRate { get; set; }

    /// <summary>
    /// Cache hit rate as a decimal (0.0-1.0)
    /// </summary>
    public double HitRateDecimal { get; set; }

    /// <summary>
    /// Total number of requests
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// Number of cache hits
    /// </summary>
    public int CacheHits { get; set; }

    /// <summary>
    /// Number of cache misses
    /// </summary>
    public int CacheMisses { get; set; }

    /// <summary>
    /// Formatted last refresh timestamp
    /// </summary>
    public string LastRefresh { get; set; } = "Never";

    /// <summary>
    /// Last refresh as nullable DateTime
    /// </summary>
    public DateTime? LastRefreshUtc { get; set; }
}

/// <summary>
/// Keys section of the stats response
/// </summary>
public class JwksKeysInfo
{
    /// <summary>
    /// Total number of keys
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Detailed information about each key
    /// </summary>
    public JwksKeyDetail[] KeyDetails { get; set; } = [];
}

/// <summary>
/// Response for the JWKS stats endpoint (admin only)
/// </summary>
public class JwksStatsResponse
{
    /// <summary>
    /// Detailed cache performance statistics
    /// </summary>
    public JwksCacheStats Cache { get; set; } = new();

    /// <summary>
    /// Information about loaded signing keys
    /// </summary>
    public JwksKeysInfo Keys { get; set; } = new();

    /// <summary>
    /// UTC timestamp of the stats query
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Response for JWKS cache clear operation
/// </summary>
public class JwksCacheClearResponse
{
    /// <summary>
    /// Result message
    /// </summary>
    /// <example>JWKS cache cleared successfully</example>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of the operation
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Response for JWKS cache refresh operation
/// </summary>
public class JwksCacheRefreshResponse
{
    /// <summary>
    /// Result message
    /// </summary>
    /// <example>JWKS cache refreshed successfully</example>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Number of keys loaded after refresh
    /// </summary>
    /// <example>1</example>
    public int KeyCount { get; set; }

    /// <summary>
    /// Available key IDs after refresh
    /// </summary>
    public string[] AvailableKeyIds { get; set; } = [];

    /// <summary>
    /// UTC timestamp of the operation
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Error response for JWKS endpoints
/// </summary>
public class JwksErrorResponse
{
    /// <summary>
    /// Error status or type
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// JWKS endpoint status
    /// </summary>
    public string? JwksEndpoint { get; set; }

    /// <summary>
    /// Error description
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Detailed error message
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// UTC timestamp of the error
    /// </summary>
    public DateTime Timestamp { get; set; }
}
