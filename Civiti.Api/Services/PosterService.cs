using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Domain.Localization;
using Civiti.Domain.Entities;
using Civiti.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Civiti.Api.Services;

/// <summary>
/// Service for generating printable PDF posters with QR codes for civic issues
/// </summary>
public class PosterService(
    IMemoryCache cache,
    CivitiDbContext context,
    PosterConfiguration config,
    ILogger<PosterService> logger) : IPosterService
{
    // Brand colors from the design system
    private const string OxfordBlue = "#14213D";
    private const string OrangeWeb = "#FCA311";
    private const string White = "#FFFFFF";

    /// <inheritdoc />
    public async Task<(byte[] PdfBytes, string FileName)?> GeneratePosterAsync(Guid issueId)
    {
        var cacheKey = $"poster:{issueId}";

        if (cache.TryGetValue(cacheKey, out (byte[] PdfBytes, string FileName) cached))
        {
            logger.LogDebug("Returning cached poster for issue {IssueId}", issueId);
            return cached;
        }

        (byte[] PdfBytes, string FileName)? result = await GeneratePosterInternalAsync(issueId);

        if (result != null)
        {
            MemoryCacheEntryOptions cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(config.CacheDurationMinutes));
            cache.Set(cacheKey, result.Value, cacheOptions);
        }

        return result;
    }

    private async Task<(byte[] PdfBytes, string FileName)?> GeneratePosterInternalAsync(Guid issueId)
    {
        // Get issue details - only allow Active issues
        var issue = await context.Issues
            .Where(i => i.Id == issueId && i.Status == IssueStatus.Active)
            .Select(i => new { i.Id, i.Title })
            .FirstOrDefaultAsync();

        if (issue == null)
        {
            logger.LogWarning("Issue {IssueId} not found or not active for poster generation", issueId);
            return null;
        }

        logger.LogInformation("Generating poster for issue {IssueId}: {Title}", issueId, issue.Title);

        // Generate QR code
        var issueUrl = $"{config.FrontendBaseUrl.TrimEnd('/')}/issue/{issueId}";
        var qrCodeBytes = GenerateQrCode(issueUrl);

        // Generate PDF
        var pdfBytes = GeneratePdf(issue.Title, qrCodeBytes);

        // Generate filename with sanitized title
        var sanitizedTitle = SanitizeFileName(issue.Title);
        var fileName = $"civica-poster-{sanitizedTitle}-{issueId:N}.pdf";

        return (pdfBytes, fileName);
    }

    private byte[] GenerateQrCode(string url)
    {
        using QRCodeGenerator qrGenerator = new();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using PngByteQRCode qrCode = new(qrCodeData);
        return qrCode.GetGraphic(config.QrSizePixels / 33); // pixels per module
    }

    private static byte[] GeneratePdf(string issueTitle, byte[] qrCodeBytes)
    {
        // Configure QuestPDF license (Community license for < $1M revenue)
        QuestPDF.Settings.License = LicenseType.Community;

        Document document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);

                page.Content().Column(column =>
                {
                    // Issue title section (top)
                    column.Item().Height(100).Background(OxfordBlue).Padding(15).AlignCenter().AlignMiddle()
                        .Text(text =>
                        {
                            text.AlignCenter();
                            text.Span(TruncateTitle(issueTitle))
                                .FontSize(20)
                                .Bold()
                                .FontColor(White);
                        });

                    // Call-to-action section
                    column.Item().Height(70).Padding(10).AlignCenter().AlignMiddle()
                        .Text(PosterLocalization.CallToAction)
                        .FontSize(20)
                        .Bold()
                        .FontColor(OrangeWeb);

                    // QR Code section (centered, fixed size to fit on one page)
                    column.Item().Height(600).AlignCenter().AlignMiddle().Padding(20)
                        .Image(qrCodeBytes)
                        .FitArea();

                    // Footer with branding
                    column.Item().Height(72).Background(OxfordBlue).AlignCenter().AlignMiddle()
                        .Text(PosterLocalization.BrandName)
                        .FontSize(28)
                        .Bold()
                        .FontColor(White);
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string TruncateTitle(string title)
    {
        const int maxLength = 150;
        if (title.Length <= maxLength)
            return title;

        return title[..(maxLength - 3)] + "...";
    }

    private static string SanitizeFileName(string title)
    {
        // Take first 30 chars, replace invalid chars
        var sanitized = title.Length > 30 ? title[..30] : title;

        // Replace invalid filename characters and spaces
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '-');
        }
        sanitized = sanitized.Replace(' ', '-');

        // Remove consecutive dashes and trim
        while (sanitized.Contains("--"))
        {
            sanitized = sanitized.Replace("--", "-");
        }

        return sanitized.Trim('-').ToLowerInvariant();
    }
}
