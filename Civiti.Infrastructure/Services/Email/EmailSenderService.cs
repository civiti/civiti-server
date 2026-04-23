using Civiti.Infrastructure.Configuration;
using Civiti.Application.Services;
using Resend;

namespace Civiti.Infrastructure.Services.Email;

/// <summary>
/// Sends emails via the Resend SDK. Gracefully no-ops when API key is not configured.
/// </summary>
public class EmailSenderService(
    ResendConfiguration config,
    IResend resendClient,
    ILogger<EmailSenderService> logger) : IEmailSenderService
{
    public async Task<bool> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (!config.IsConfigured)
        {
            logger.LogDebug("Resend not configured — skipping email to {To}: {Subject}", to, subject);
            return false;
        }

        try
        {
            EmailMessage message = new()
            {
                From = config.FromEmail
            };
            message.To.Add(to);
            message.Subject = subject;
            message.HtmlBody = htmlBody;

            await resendClient.EmailSendAsync(message, cancellationToken);

            logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
            return false;
        }
    }
}
