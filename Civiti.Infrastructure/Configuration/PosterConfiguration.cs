namespace Civiti.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for poster generation
/// </summary>
public class PosterConfiguration
{
    /// <summary>
    /// The base URL of the frontend application used for generating QR code links
    /// </summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>
    /// The size of the QR code in pixels (default: 300)
    /// </summary>
    public int QrSizePixels { get; set; } = 300;

    /// <summary>
    /// Duration to cache generated posters (in minutes)
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 15;
}
