using Civiti.Api.Infrastructure.Email;
using Civiti.Application.Email.Models;
using Civiti.Application.Services;
using static Civiti.Api.Infrastructure.Email.EmailDataKeys;

namespace Civiti.Api.Services;

/// <summary>
/// Renders email HTML by combining EmailTemplates content with the EmailLayout wrapper
/// </summary>
public class EmailTemplateService : IEmailTemplateService
{
    public (string Subject, string HtmlBody) Render(EmailNotificationType type, Dictionary<string, string> data)
    {
        var (subject, bodyHtml) = EmailTemplates.Get(type, data);

        var ctaUrl = data.GetValueOrDefault(CtaUrl);
        var ctaText = data.GetValueOrDefault(CtaText);

        var html = EmailLayout.Wrap(subject, bodyHtml, ctaUrl, ctaText);

        return (subject, html);
    }
}
