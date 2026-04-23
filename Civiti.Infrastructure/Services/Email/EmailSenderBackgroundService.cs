using System.Threading.Channels;
using Civiti.Application.Email.Models;
using Civiti.Application.Services;

namespace Civiti.Infrastructure.Services.Email;

/// <summary>
/// Background service that drains the email channel and sends emails asynchronously.
/// Uses IServiceScopeFactory because IEmailSenderService depends on transient IResend.
/// </summary>
public class EmailSenderBackgroundService(
    ChannelReader<EmailNotification> channelReader,
    IServiceScopeFactory scopeFactory,
    ILogger<EmailSenderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Email sender background service starting");

        try
        {
            await foreach (EmailNotification notification in channelReader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using IServiceScope scope = scopeFactory.CreateScope();
                    var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSenderService>();

                    await emailSender.SendEmailAsync(
                        notification.To,
                        notification.Subject,
                        notification.HtmlBody,
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send {Type} email to {To}",
                        notification.Type, notification.To);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email sender background service encountered a fatal error");
        }

        logger.LogInformation("Email sender background service stopping");
    }
}
