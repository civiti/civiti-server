using System.ComponentModel.DataAnnotations;

namespace Civiti.Api.Infrastructure.Configuration;

/// <summary>
/// Configuration options for JWT validation with JWKS support
/// </summary>
public class JwtValidationOptions
{
    /// <summary>
    /// The JWKS endpoint URL for fetching public keys
    /// Example: https://your-project.supabase.co/auth/v1/.well-known/jwks.json
    /// </summary>
    [Required]
    public string JwksUrl { get; set; } = string.Empty;

    /// <summary>
    /// The expected JWT issuer
    /// Example: https://your-project.supabase.co/auth/v1
    /// </summary>
    [Required]
    public string ValidIssuer { get; set; } = string.Empty;

    /// <summary>
    /// The expected JWT audience
    /// Typically "authenticated" for Supabase
    /// </summary>
    [Required]
    public string ValidAudience { get; set; } = string.Empty;

    /// <summary>
    /// How long to cache the JWKS in milliseconds
    /// Default: 1 hour (3600000ms)
    /// </summary>
    public int JwksCacheTtlMs { get; set; } = 60 * 60 * 1000;

    /// <summary>
    /// Clock skew tolerance for token expiry validation
    /// Default: 60 seconds
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether to require HTTPS for metadata endpoints
    /// Should be true in production
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Maximum number of retries when fetching JWKS fails
    /// Default: 3 retries
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between JWKS fetch retries in milliseconds
    /// Default: 1 second (1000ms)
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// HTTP timeout for JWKS requests in seconds
    /// Default: 30 seconds
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 30;
}