using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Models.Responses.Jwks;
using Civiti.Api.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace Civiti.Api.Endpoints;

/// <summary>
/// Endpoints for JWKS management and monitoring
/// </summary>
public static class JwksEndpoints
{
    /// <summary>
    /// Maps JWKS-related endpoints
    /// </summary>
    /// <param name="app">The web application</param>
    public static void MapJwksEndpoints(this WebApplication app)
    {
        RouteGroupBuilder jwksGroup = app.MapGroup("/api/jwks")
            .WithTags("JWKS");

        // JWKS health check endpoint
        jwksGroup.MapGet("/health", GetJwksHealth)
            .WithName("GetJwksHealth")
            .WithSummary("Check JWKS service health and cache statistics")
            .WithDescription("Returns health status of the JWKS service including cache statistics and connectivity to Supabase JWKS endpoint")
            .Produces<JwksHealthResponse>()
            .Produces<JwksErrorResponse>(503);

        // JWKS cache stats endpoint (admin only)
        jwksGroup.MapGet("/stats", GetJwksStats)
            .WithName("GetJwksStats")
            .WithSummary("Get detailed JWKS cache statistics")
            .WithDescription("Returns detailed cache statistics for monitoring JWKS performance (admin only)")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .Produces<JwksStatsResponse>()
            .Produces(401)
            .Produces(403);

        // Clear JWKS cache endpoint (admin only)
        jwksGroup.MapPost("/cache/clear", ClearJwksCache)
            .WithName("ClearJwksCache")
            .WithSummary("Clear JWKS cache")
            .WithDescription("Manually clear the JWKS cache to force refresh from Supabase (admin only)")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .Produces<JwksCacheClearResponse>()
            .Produces(401)
            .Produces(403);

        // Refresh JWKS cache endpoint (admin only)
        jwksGroup.MapPost("/cache/refresh", RefreshJwksCache)
            .WithName("RefreshJwksCache")
            .WithSummary("Refresh JWKS cache")
            .WithDescription("Manually refresh the JWKS cache from Supabase (admin only)")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .Produces<JwksCacheRefreshResponse>()
            .Produces(401)
            .Produces(403)
            .Produces<JwksErrorResponse>(503);
    }

    /// <summary>
    /// Gets JWKS service health status
    /// </summary>
    private static async Task<IResult> GetJwksHealth(IJwksManager jwksManager, ILogger<Program> logger)
    {
        try
        {
            // Test JWKS connectivity with a quick fetch
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
            JsonWebKeySet jwks = await jwksManager.GetJwksAsync(false, cts.Token);

            (var hitRate, DateTime? lastRefresh, var totalRequests) = jwksManager.GetCacheStats();

            var health = new JwksHealthResponse
            {
                Status = "Healthy",
                JwksEndpoint = "Connected",
                KeyCount = jwks.Keys.Count,
                Cache = new JwksCacheInfo
                {
                    HitRate = Math.Round(hitRate * 100, 2),
                    LastRefresh = lastRefresh?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Never",
                    TotalRequests = totalRequests
                },
                Timestamp = DateTime.UtcNow,
                AvailableKeyIds = jwks.Keys.Select(k => k.Kid).Where(kid => !string.IsNullOrEmpty(kid)).ToArray()!
            };

            return Results.Ok(health);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("JWKS health check timed out");
            return Results.Json(new JwksErrorResponse
            {
                Status = "Unhealthy",
                JwksEndpoint = "Timeout",
                Error = "JWKS endpoint did not respond within 10 seconds",
                Timestamp = DateTime.UtcNow
            }, statusCode: 503);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JWKS health check failed");
            return Results.Json(new JwksErrorResponse
            {
                Status = "Unhealthy",
                JwksEndpoint = "Error",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            }, statusCode: 503);
        }
    }

    /// <summary>
    /// Gets detailed JWKS cache statistics
    /// </summary>
    private static async Task<IResult> GetJwksStats(IJwksManager jwksManager)
    {
        try
        {
            (var hitRate, DateTime? lastRefresh, var totalRequests) = jwksManager.GetCacheStats();

            // Get current JWKS without forcing refresh to see cached data
            JsonWebKeySet jwks = await jwksManager.GetJwksAsync(false, CancellationToken.None);

            var stats = new JwksStatsResponse
            {
                Cache = new JwksCacheStats
                {
                    HitRate = Math.Round(hitRate * 100, 2),
                    HitRateDecimal = hitRate,
                    TotalRequests = totalRequests,
                    CacheHits = (int)(totalRequests * hitRate),
                    CacheMisses = totalRequests - (int)(totalRequests * hitRate),
                    LastRefresh = lastRefresh?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Never",
                    LastRefreshUtc = lastRefresh
                },
                Keys = new JwksKeysInfo
                {
                    Total = jwks.Keys.Count,
                    KeyDetails = jwks.Keys.Select(k => new JwksKeyDetail
                    {
                        Kid = k.Kid,
                        Kty = k.Kty,
                        Use = k.Use,
                        Alg = k.Alg,
                        KeyOps = k.KeyOps?.ToArray()
                    }).ToArray()
                },
                Timestamp = DateTime.UtcNow
            };

            return Results.Ok(stats);
        }
        catch (Exception ex)
        {
            return Results.Json(new JwksErrorResponse
            {
                Error = "Failed to retrieve JWKS statistics",
                Message = ex.Message,
                Timestamp = DateTime.UtcNow
            }, statusCode: 500);
        }
    }

    /// <summary>
    /// Clears JWKS cache
    /// </summary>
    private static IResult ClearJwksCache(IJwksManager jwksManager, ILogger<Program> logger)
    {
        try
        {
            jwksManager.ClearCache();
            logger.LogInformation("JWKS cache cleared by admin");

            return Results.Ok(new JwksCacheClearResponse
            {
                Message = "JWKS cache cleared successfully",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear JWKS cache");
            return Results.Json(new JwksErrorResponse
            {
                Error = "Failed to clear JWKS cache",
                Message = ex.Message,
                Timestamp = DateTime.UtcNow
            }, statusCode: 500);
        }
    }

    /// <summary>
    /// Refreshes JWKS cache
    /// </summary>
    private static async Task<IResult> RefreshJwksCache(IJwksManager jwksManager, ILogger<Program> logger)
    {
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            JsonWebKeySet jwks = await jwksManager.GetJwksAsync(true, cts.Token);

            logger.LogInformation("JWKS cache refreshed by admin - {KeyCount} keys loaded", jwks.Keys.Count);

            return Results.Ok(new JwksCacheRefreshResponse
            {
                Message = "JWKS cache refreshed successfully",
                KeyCount = jwks.Keys.Count,
                AvailableKeyIds = jwks.Keys.Select(k => k.Kid).Where(kid => !string.IsNullOrEmpty(kid)).ToArray()!,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("JWKS cache refresh timed out");
            return Results.Json(new JwksErrorResponse
            {
                Error = "JWKS refresh timed out",
                Message = "Supabase JWKS endpoint did not respond within 30 seconds",
                Timestamp = DateTime.UtcNow
            }, statusCode: 503);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh JWKS cache");
            return Results.Json(new JwksErrorResponse
            {
                Error = "Failed to refresh JWKS cache",
                Message = ex.Message,
                Timestamp = DateTime.UtcNow
            }, statusCode: 500);
        }
    }
}
