namespace Civiti.Api.Services.Interfaces;

/// <summary>
/// Low-level email sending abstraction wrapping the Resend SDK
/// </summary>
public interface IEmailSenderService
{
    Task<bool> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
