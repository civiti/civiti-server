using System.Net;

namespace Civiti.Infrastructure.Email.Templates;

/// <summary>
/// Shared HTML email layout with Civiti branding.
/// Inline CSS for maximum email client compatibility.
/// </summary>
public static class EmailLayout
{
    /// <summary>
    /// Wraps content in the branded Civiti email layout
    /// </summary>
    public static string Wrap(string title, string bodyHtml, string? ctaUrl = null, string? ctaText = null)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeCtaUrl = WebUtility.HtmlEncode(ctaUrl ?? "");
        var safeCtaText = WebUtility.HtmlEncode(ctaText ?? "");

        var ctaBlock = !string.IsNullOrEmpty(ctaUrl) && !string.IsNullOrEmpty(ctaText)
            ? $"""
               <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin: 24px auto;">
                 <tr>
                   <td style="border-radius: 6px; background-color: #FCA311;">
                     <a href="{safeCtaUrl}" target="_blank"
                        style="display: inline-block; padding: 14px 32px; font-family: 'Fira Sans', Arial, sans-serif;
                               font-size: 16px; font-weight: 600; color: #14213D; text-decoration: none;">
                       {safeCtaText}
                     </a>
                   </td>
                 </tr>
               </table>
               """
            : "";

        return $"""
                <!DOCTYPE html>
                <html lang="ro">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1.0">
                  <title>{safeTitle}</title>
                </head>
                <body style="margin: 0; padding: 0; background-color: #E5E5E5; font-family: 'Fira Sans', Arial, sans-serif;">
                  <table role="presentation" cellspacing="0" cellpadding="0" border="0" width="100%"
                         style="background-color: #E5E5E5; padding: 24px 0;">
                    <tr>
                      <td align="center">
                        <table role="presentation" cellspacing="0" cellpadding="0" border="0"
                               style="max-width: 600px; width: 100%; background-color: #FFFFFF; border-radius: 8px; overflow: hidden;">
                          <!-- Header -->
                          <tr>
                            <td style="background-color: #14213D; padding: 24px 32px; text-align: center;">
                              <h1 style="margin: 0; color: #FFFFFF; font-family: 'Fira Sans', Arial, sans-serif;
                                         font-size: 24px; font-weight: 700; letter-spacing: 1px;">
                                CIVITI
                              </h1>
                              <p style="margin: 4px 0 0; color: #FCA311; font-size: 12px; font-weight: 500; letter-spacing: 0.5px;">
                                Platforma Civică a Cetățenilor
                              </p>
                            </td>
                          </tr>
                          <!-- Body -->
                          <tr>
                            <td style="padding: 32px;">
                              <h2 style="margin: 0 0 16px; color: #14213D; font-family: 'Fira Sans', Arial, sans-serif;
                                         font-size: 20px; font-weight: 600;">
                                {safeTitle}
                              </h2>
                              <div style="color: #333333; font-size: 15px; line-height: 1.6;">
                                {bodyHtml}
                              </div>
                              {ctaBlock}
                            </td>
                          </tr>
                          <!-- Footer -->
                          <tr>
                            <td style="background-color: #F5F5F5; padding: 20px 32px; border-top: 1px solid #E5E5E5;">
                              <p style="margin: 0; color: #666666; font-size: 12px; line-height: 1.5; text-align: center;">
                                Acest email a fost trimis de platforma Civiti.<br>
                                &copy; {DateTime.UtcNow.Year} Civiti. Toate drepturile rezervate.
                              </p>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>
                """;
    }
}