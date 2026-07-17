namespace Civiti.Application.Services;

/// <summary>
/// Persists the AI-composed petition argument core per issue so repeated opens (any user, any
/// instance, across restarts) don't re-hit the Claude API. Only the expensive core is cached;
/// the deterministic scaffold is re-assembled on every request so the live photo count and
/// documentation link stay current.
/// </summary>
public interface IPetitionBodyCacheStore
{
    /// <summary>
    /// Returns the cached core for an issue, or <c>null</c> when nothing has been generated yet.
    /// Freshness (content-hash match and TTL) is the caller's responsibility.
    /// </summary>
    Task<PetitionCoreCacheEntry?> GetAsync(Guid issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores (or overwrites) the cached core for an issue. Touches only the three cache columns,
    /// never the issue's own fields or <c>UpdatedAt</c>.
    /// </summary>
    Task SetAsync(Guid issueId, string core, string contentHash, DateTime generatedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>A cached petition core with the fingerprint and timestamp needed to judge freshness.</summary>
public sealed record PetitionCoreCacheEntry(string Core, string ContentHash, DateTime GeneratedAtUtc);
