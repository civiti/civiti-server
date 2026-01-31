using Microsoft.IdentityModel.Tokens;

namespace Civiti.Api.Services.Interfaces;

/// <summary>
/// Service interface for managing JWKS (JSON Web Key Set) operations
/// Handles fetching, caching, and key resolution for JWT validation
/// </summary>
public interface IJwksManager
{
    /// <summary>
    /// Gets the JWKS from cache or fetches it from the remote endpoint
    /// </summary>
    /// <param name="forceRefresh">Whether to bypass cache and force a fresh fetch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The JSON Web Key Set</returns>
    Task<JsonWebKeySet> GetJwksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a specific key by its key ID (kid)
    /// Implements retry logic for key rotation scenarios
    /// </summary>
    /// <param name="kid">The key ID to search for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The JSON Web Key if found, null otherwise</returns>
    Task<JsonWebKey?> GetKeyForKidAsync(string kid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available signing keys from the JWKS
    /// Used for key resolver scenarios where multiple keys may be valid
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of available signing keys</returns>
    Task<IEnumerable<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the JWKS cache
    /// Useful for manual cache invalidation or testing scenarios
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Gets cache statistics for monitoring purposes
    /// </summary>
    /// <returns>Cache hit rate and last refresh time</returns>
    (double HitRate, DateTime? LastRefresh, int TotalRequests) GetCacheStats();

    /// <summary>
    /// Gets the cached JWKS synchronously without fetching from remote
    /// Used by the IssuerSigningKeyResolver to avoid async deadlocks
    /// </summary>
    /// <returns>The cached JWKS if available, null otherwise</returns>
    JsonWebKeySet? GetCachedJwks();

    /// <summary>
    /// Gets the cached signing keys synchronously without fetching from remote
    /// Used by the IssuerSigningKeyResolver to avoid async deadlocks
    /// </summary>
    /// <returns>The cached signing keys if available, empty collection otherwise</returns>
    IEnumerable<SecurityKey> GetCachedSigningKeys();
}