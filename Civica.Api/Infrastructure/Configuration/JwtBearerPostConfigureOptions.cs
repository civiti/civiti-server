using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Civica.Api.Services.Interfaces;

namespace Civica.Api.Infrastructure.Configuration;

/// <summary>
/// Post-configure options for JWT Bearer authentication with JWKS support
/// This class properly injects dependencies without creating a service provider anti-pattern
/// </summary>
public class JwtBearerPostConfigureOptions : IPostConfigureOptions<JwtBearerOptions>
{
    private readonly IJwksManager _jwksManager;
    private readonly IOptions<JwtValidationOptions> _jwtValidationOptions;
    private readonly ILogger<JwtBearerPostConfigureOptions> _logger;

    public JwtBearerPostConfigureOptions(
        IJwksManager jwksManager,
        IOptions<JwtValidationOptions> jwtValidationOptions,
        ILogger<JwtBearerPostConfigureOptions> logger)
    {
        _jwksManager = jwksManager ?? throw new ArgumentNullException(nameof(jwksManager));
        _jwtValidationOptions = jwtValidationOptions ?? throw new ArgumentNullException(nameof(jwtValidationOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        // Only configure for the default scheme
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwtValidationOptions = _jwtValidationOptions.Value;

        // Override the key resolver with JWKS support (synchronous cache lookup only)
        options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
        {
            try
            {
                _logger.LogDebug("Resolving signing key for kid: {Kid}", kid);

                // Try to find the specific key by kid in JWKS cache
                if (!string.IsNullOrEmpty(kid))
                {
                    var cachedJwks = _jwksManager.GetCachedJwks();

                    if (cachedJwks != null)
                    {
                        var jwk = cachedJwks.Keys.FirstOrDefault(k => k.Kid == kid);
                        if (jwk != null)
                        {
                            _logger.LogDebug("Found cached JWKS key for kid: {Kid}", kid);
                            return new[] { jwk };
                        }
                    }
                }

                // Fallback: return all cached signing keys for validation attempt
                _logger.LogDebug("Kid not found in cache, returning all cached signing keys");
                var cachedKeys = _jwksManager.GetCachedSigningKeys();

                if (cachedKeys != null && cachedKeys.Any())
                {
                    _logger.LogDebug("Returning {Count} cached signing keys for validation", cachedKeys.Count());
                    return cachedKeys;
                }

                _logger.LogWarning("No signing keys available for JWT validation - JWKS may not be loaded yet");
                return Enumerable.Empty<SecurityKey>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving JWKS signing key for kid: {Kid}", kid);
                return Enumerable.Empty<SecurityKey>();
            }
        };

        _logger.LogInformation("JWKS key resolver configured successfully with synchronous cache lookup");
    }
}