namespace Civiti.Api.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for Resend email service
/// </summary>
public class ResendConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "Civiti <noreply@civiti.ro>";
    public string FrontendBaseUrl { get; set; } = "http://localhost:4200";
    public int DebounceMinutes { get; set; } = 5;
    public int ChannelCapacity { get; set; } = 10_000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
