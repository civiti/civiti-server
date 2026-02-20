namespace Civiti.Api.Models.Responses.Health;

/// <summary>
/// Response returned by the health check endpoint
/// </summary>
public class HealthCheckResponse
{
    /// <summary>
    /// Overall health status: Healthy or Degraded
    /// </summary>
    /// <example>Healthy</example>
    public string Status { get; set; } = "Healthy";

    /// <summary>
    /// UTC timestamp of the health check
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// API version
    /// </summary>
    /// <example>1.0.0</example>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Database connectivity status: connected, disconnected, or timeout
    /// </summary>
    /// <example>connected</example>
    public string Database { get; set; } = "unknown";

    /// <summary>
    /// Database error message if connectivity failed
    /// </summary>
    public string? DatabaseError { get; set; }

    /// <summary>
    /// Supabase authentication service connectivity status
    /// </summary>
    /// <example>connected</example>
    public string Supabase { get; set; } = "unknown";

    /// <summary>
    /// Runtime environment name (Development, Production, etc.)
    /// </summary>
    /// <example>Production</example>
    public string Environment { get; set; } = string.Empty;
}
