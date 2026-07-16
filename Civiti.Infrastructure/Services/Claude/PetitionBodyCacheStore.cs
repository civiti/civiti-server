using Civiti.Application.Services;
using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Infrastructure.Services.Claude;

/// <summary>
/// <see cref="IPetitionBodyCacheStore"/> backed by the three cache columns on the Issues table.
/// Reads are a lightweight projection; writes use <c>ExecuteUpdateAsync</c> so only the cache
/// columns change (no entity tracking, no risk of clobbering concurrent edits to other fields,
/// and <c>UpdatedAt</c> is left untouched so caching never looks like an issue edit).
/// </summary>
public sealed class PetitionBodyCacheStore(CivitiDbContext context) : IPetitionBodyCacheStore
{
    /// <inheritdoc />
    public async Task<PetitionCoreCacheEntry?> GetAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        var row = await context.Issues
            .AsNoTracking()
            .Where(i => i.Id == issueId)
            .Select(i => new { i.PetitionBodyCore, i.PetitionBodyContentHash, i.PetitionBodyGeneratedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (row?.PetitionBodyCore is null || row.PetitionBodyContentHash is null || row.PetitionBodyGeneratedAt is null)
        {
            return null;
        }

        return new PetitionCoreCacheEntry(row.PetitionBodyCore, row.PetitionBodyContentHash, row.PetitionBodyGeneratedAt.Value);
    }

    /// <inheritdoc />
    public Task SetAsync(Guid issueId, string core, string contentHash, DateTime generatedAtUtc, CancellationToken cancellationToken = default) =>
        context.Issues
            .Where(i => i.Id == issueId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(i => i.PetitionBodyCore, core)
                .SetProperty(i => i.PetitionBodyContentHash, contentHash)
                .SetProperty(i => i.PetitionBodyGeneratedAt, generatedAtUtc), cancellationToken);
}
