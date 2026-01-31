using Civiti.Api.Models.Email;

namespace Civiti.Api.Services.Interfaces;

/// <summary>
/// Renders email HTML from notification type and template data
/// </summary>
public interface IEmailTemplateService
{
    (string Subject, string HtmlBody) Render(EmailNotificationType type, Dictionary<string, string> data);
}
