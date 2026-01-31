namespace Civiti.Api.Services.Interfaces;

/// <summary>
/// Service for generating printable PDF posters with QR codes for civic issues
/// </summary>
public interface IPosterService
{
    /// <summary>
    /// Generates a PDF poster with a QR code linking to the specified issue
    /// </summary>
    /// <param name="issueId">The ID of the issue to generate a poster for</param>
    /// <returns>
    /// A tuple containing the PDF bytes and filename if successful, or null if the issue
    /// is not found or not publicly visible
    /// </returns>
    Task<(byte[] PdfBytes, string FileName)?> GeneratePosterAsync(Guid issueId);
}
