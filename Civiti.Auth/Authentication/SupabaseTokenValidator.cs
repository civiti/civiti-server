using System.Security.Claims;
using Civiti.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Civiti.Auth.Authentication;

/// <summary>
/// Validates the Supabase access JWT we receive from the /auth/v1/token PKCE exchange.
/// Caches Supabase's published JWKS in memory (6 h TTL) and validates issuer + signature +
/// lifetime. Audience is intentionally unchecked — Supabase JWTs use a fixed
/// <c>"authenticated"</c> audience that gives us no useful signal here.
/// </summary>
public sealed class SupabaseTokenValidator(
    SupabaseConfiguration config,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<SupabaseTokenValidator> logger)
{
    private const string JwksCacheKey = "Civiti.Auth.SupabaseJwks";
    private const string JwksFailureCacheKey = "Civiti.Auth.SupabaseJwks.Failure";
    private static readonly TimeSpan JwksCacheTtl = TimeSpan.FromHours(6);
    // Negative-cache fetch failures briefly so a Supabase outage doesn't generate one upstream
    // request per inbound JWT; the value is short enough to recover quickly once Supabase returns.
    private static readonly TimeSpan JwksFailureCacheTtl = TimeSpan.FromSeconds(30);
    // Collapse concurrent cache-miss fetches so a TTL expiry under burst traffic produces exactly
    // one Supabase round-trip instead of one per request. The semaphore is process-wide because
    // the validator itself is registered as a singleton.
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    public async Task<ClaimsPrincipal?> ValidateAsync(string supabaseJwt, CancellationToken cancellationToken)
    {
        var jwks = await GetJwksAsync(cancellationToken);
        if (jwks is null)
        {
            logger.LogWarning("Cannot validate Supabase JWT: JWKS unavailable");
            return null;
        }

        var validationParameters = new TokenValidationParameters
        {
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidIssuer = $"{config.Url.TrimEnd('/')}/auth/v1",
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(supabaseJwt, validationParameters);
        if (!result.IsValid)
        {
            logger.LogWarning("Supabase JWT validation failed: {Reason}",
                result.Exception?.GetType().Name ?? "unknown");
            return null;
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    private async Task<JsonWebKeySet?> GetJwksAsync(CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(JwksCacheKey, out JsonWebKeySet? cached) && cached is not null)
        {
            return cached;
        }

        if (memoryCache.TryGetValue(JwksFailureCacheKey, out _))
        {
            return null;
        }

        await FetchGate.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring the gate — another caller may have populated the cache
            // while we were waiting, in which case a duplicate fetch would be wasted work.
            if (memoryCache.TryGetValue(JwksCacheKey, out cached) && cached is not null)
            {
                return cached;
            }
            if (memoryCache.TryGetValue(JwksFailureCacheKey, out _))
            {
                return null;
            }

            try
            {
                using var http = httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(5);
                var jwksJson = await http.GetStringAsync(
                    $"{config.Url.TrimEnd('/')}/auth/v1/.well-known/jwks.json",
                    cancellationToken);

                var jwks = new JsonWebKeySet(jwksJson);
                memoryCache.Set(JwksCacheKey, jwks, JwksCacheTtl);
                return jwks;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch Supabase JWKS from {Url}", config.Url);
                memoryCache.Set(JwksFailureCacheKey, true, JwksFailureCacheTtl);
                return null;
            }
        }
        finally
        {
            FetchGate.Release();
        }
    }
}
