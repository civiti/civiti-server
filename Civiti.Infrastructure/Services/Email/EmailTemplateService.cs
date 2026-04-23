using Civiti.Infrastructure.Email.Templates;
using Civiti.Application.Email.Models;
using Civiti.Application.Services;
using static Civiti.Infrastructure.Email.Templates.EmailDataKeys;

namespace Civiti.Infrastructure.Services.Email;

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
