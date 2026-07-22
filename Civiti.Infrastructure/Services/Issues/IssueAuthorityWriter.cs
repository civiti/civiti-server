using Civiti.Application.Requests.Issues;
using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Civiti.Domain.Exceptions;
using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Infrastructure.Services.Issues;

/// <summary>
/// Validates and materialises the authorities linked to an issue.
/// <para>
/// Shared by issue creation and owner edits. The rules live here rather than on the request DTO
/// because the MCP tools call the service directly and never pass through HTTP model
/// validation — a DTO-only rule would simply not run for them.
/// </para>
/// </summary>
internal static class IssueAuthorityWriter
{
    /// <summary>
    /// Builds the authority links for an issue from the complete desired set.
    /// Each entry is either a predefined authority or a custom name/email pair, never both and
    /// never neither.
    /// </summary>
    /// <exception cref="IssueContentValidationException">
    /// Too many entries, duplicates, an entry that is neither or both, or a predefined
    /// authority that does not exist or is inactive.
    /// </exception>
    public static async Task<List<IssueAuthority>> MaterializeAsync(
        CivitiDbContext context,
        Guid issueId,
        IEnumerable<IssueAuthorityInput>? inputs,
        DateTime timestamp)
    {
        List<IssueAuthorityInput> authorities = (inputs ?? []).Where(a => a != null).ToList();

        if (authorities.Count == 0)
        {
            return [];
        }

        if (authorities.Count > IssueValidationLimits.MaxAuthorityCount)
        {
            throw new IssueContentValidationException(
                $"A maximum of {IssueValidationLimits.MaxAuthorityCount} authorities are allowed.");
        }

        List<Guid> predefinedIds = authorities
            .Where(a => a.AuthorityId.HasValue)
            .Select(a => a.AuthorityId!.Value)
            .ToList();

        if (predefinedIds.Count != predefinedIds.Distinct().Count())
        {
            throw new IssueContentValidationException("Duplicate authority IDs are not allowed");
        }

        List<string> customEmails = authorities
            .Where(a => !a.AuthorityId.HasValue && !string.IsNullOrWhiteSpace(a.CustomEmail))
            .Select(a => a.CustomEmail!.ToLowerInvariant())
            .ToList();

        if (customEmails.Count != customEmails.Distinct().Count())
        {
            throw new IssueContentValidationException("Duplicate custom authority emails are not allowed");
        }

        List<IssueAuthority> links = [];

        foreach (IssueAuthorityInput input in authorities)
        {
            var hasPredefined = input.AuthorityId.HasValue;
            var hasCustom = !string.IsNullOrWhiteSpace(input.CustomName) &&
                            !string.IsNullOrWhiteSpace(input.CustomEmail);

            if (!hasPredefined && !hasCustom)
            {
                throw new IssueContentValidationException(
                    "Each authority must have either an AuthorityId or both CustomName and CustomEmail");
            }

            if (hasPredefined && hasCustom)
            {
                throw new IssueContentValidationException(
                    "Authority cannot have both AuthorityId and custom fields");
            }

            if (hasPredefined)
            {
                var authorityExists = await context.Authorities
                    .AnyAsync(a => a.Id == input.AuthorityId && a.IsActive);

                if (!authorityExists)
                {
                    throw new IssueContentValidationException(
                        $"Authority with ID {input.AuthorityId} not found or is inactive");
                }
            }

            links.Add(new IssueAuthority
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                AuthorityId = input.AuthorityId,
                CustomName = hasPredefined ? null : input.CustomName,
                CustomEmail = hasPredefined ? null : input.CustomEmail,
                CreatedAt = timestamp
            });
        }

        return links;
    }
}
