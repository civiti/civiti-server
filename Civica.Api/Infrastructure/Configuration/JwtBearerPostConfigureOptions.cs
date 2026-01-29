using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Civica.Api.Services.Interfaces;

namespace Civica.Api.Infrastructure.Configuration;

/// <summary>
/// Post-configure options for JWT Bearer authentication with JWKS support.
/// Configures a synchronous IssuerSigningKeyResolver that reads from the JWKS cache
/// populated by <see cref="Services.JwksBackgroundService"/>.
/// </summary>
public class JwtBearerPostConfigureOptions(
    IJwksManager jwksManager,
    ILogger<JwtBearerPostConfigureOptions> logger)
    : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
            return;

        options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
        {
            try
            {
                logger.LogDebug("Resolving signing key for kid: {Kid}", kid);

                if (!string.IsNullOrEmpty(kid))
                {
                    JsonWebKeySet? cachedJwks = jwksManager.GetCachedJwks();
                    JsonWebKey? jwk = cachedJwks?.Keys.FirstOrDefault(k => k.Kid == kid);

                    if (jwk != null)
                    {
                        logger.LogDebug("Found cached JWKS key for kid: {Kid}", kid);
                        return [jwk];
                    }
                }

                List<SecurityKey> cachedKeys = jwksManager.GetCachedSigningKeys().ToList();

                if (cachedKeys.Count > 0)
                {
                    logger.LogDebug("Kid not found in cache, returning all {Count} cached signing keys", cachedKeys.Count);
                    return cachedKeys;
                }

                logger.LogWarning("No signing keys available for JWT validation - JWKS may not be loaded yet");
                return [];
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error resolving JWKS signing key for kid: {Kid}", kid);
                return [];
            }
        };

        logger.LogInformation("JWKS key resolver configured successfully");
    }
}