using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Civiti.Domain.Exceptions;

namespace Civiti.Infrastructure.Services.Issues;

/// <summary>
/// Turns a client-supplied list of photo URLs into <see cref="IssuePhoto"/> rows.
/// Shared by issue creation and owner edits so the URL guard cannot apply to only one of them.
/// </summary>
internal static class IssuePhotoWriter
{
    /// <summary>
    /// Rejects photo URLs that are unsafe to hand back to a rendering client.
    /// <para>
    /// Issue photos are echoed in <c>IssueDetailResponse.Photos</c> and any REST or MCP client
    /// that renders them naively would execute an attacker-supplied <c>javascript:</c>,
    /// <c>data:</c> or <c>file:</c> URI. Validated up front so the whole set is rejected
    /// together and a partially-validated set is never written.
    /// </para>
    /// </summary>
    /// <exception cref="IssueContentValidationException">A URL is over-length or not http(s).</exception>
    public static void ValidateUrls(IEnumerable<string>? photoUrls)
    {
        if (photoUrls is null)
        {
            return;
        }

        foreach (var url in photoUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (url.Length > IssueValidationLimits.MaxPhotoUrlLength)
            {
                throw new IssueContentValidationException(
                    $"Issue photo URL exceeds the {IssueValidationLimits.MaxPhotoUrlLength}-character limit.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? photoUri)
                || (photoUri.Scheme != Uri.UriSchemeHttp && photoUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new IssueContentValidationException(
                    "Issue photo URLs must be absolute http or https URLs.");
            }
        }
    }

    /// <summary>
    /// Builds the photo rows for an issue. Blank entries are dropped; the first surviving URL
    /// becomes the primary photo, which is the positional convention the clients rely on.
    /// Call <see cref="ValidateUrls"/> first.
    /// </summary>
    public static List<IssuePhoto> Materialize(Guid issueId, IEnumerable<string>? photoUrls, DateTime timestamp) =>
        (photoUrls ?? [])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select((url, index) => new IssuePhoto
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                Url = url,
                IsPrimary = index == 0,
                DisplayOrder = index,
                CreatedAt = timestamp
            })
            .ToList();
}
