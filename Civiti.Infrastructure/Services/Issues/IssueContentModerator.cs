using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Moderation;
using Civiti.Application.Services;
using Civiti.Domain.Exceptions;

namespace Civiti.Infrastructure.Services.Issues;

/// <summary>
/// Runs the shared moderation gate over an issue's free-text content.
/// <para>
/// Shared by creation and owner edits. Moderating only on create would be no gate at all: an
/// author could publish something benign and then edit the abusive content in, reaching exactly
/// the same read surfaces.
/// </para>
/// </summary>
internal static class IssueContentModerator
{
    /// <summary>
    /// Moderates every free-text field that surfaces unmodified through the read paths
    /// (<c>search_issues</c> / <c>get_issue</c> / <c>list_my_activity</c> and the REST
    /// equivalents). The fields are concatenated into one call to keep the cost at a single
    /// round-trip; a hit anywhere rejects the whole submission, because an issue with a
    /// moderation hit in any field is not safe to publish.
    /// <para>
    /// Call this <b>before</b> opening a database transaction — the provider round-trip takes
    /// 300 ms–2 s, and holding a pooled connection across it exhausts the pool under load.
    /// </para>
    /// </summary>
    /// <exception cref="ContentModerationException">The content was rejected.</exception>
    public static async Task EnsureAllowedAsync(
        IContentModerationService contentModerationService,
        IssueContentRequest request)
    {
        var moderationInput = string.Join(
            "\n\n",
            request.Title ?? string.Empty,
            request.Description ?? string.Empty,
            request.Address ?? string.Empty,
            request.District ?? string.Empty,
            request.DesiredOutcome ?? string.Empty,
            request.CommunityImpact ?? string.Empty);

        ContentModerationResponse result =
            await contentModerationService.ModerateContentAsync(moderationInput);

        if (!result.IsAllowed)
        {
            throw new ContentModerationException(
                result.BlockReason ?? "Content violates community guidelines");
        }
    }
}
