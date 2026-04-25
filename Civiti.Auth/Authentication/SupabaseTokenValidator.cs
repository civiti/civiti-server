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
internal sealed class SupabaseTokenValidator(
    SupabaseConfiguration config,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<SupabaseTokenValidator> logger)
{
    private const string JwksCacheKey = "Civiti.Auth.SupabaseJwks";
    private static readonly TimeSpan JwksCacheTtl = TimeSpan.FromHours(6);

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
            return null;
        }
    }
}
