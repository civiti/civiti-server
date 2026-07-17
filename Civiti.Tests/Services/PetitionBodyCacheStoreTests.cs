using Civiti.Domain.Entities;
using Civiti.Infrastructure.Services.Claude;
using Civiti.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Civiti.Tests.Services;

/// <summary>
/// Coverage for the production <see cref="PetitionBodyCacheStore"/> against a real (SQLite) DbContext,
/// pinning the behaviours the in-memory fake can't: the write touches ONLY the three cache columns
/// (never UpdatedAt or other fields), the read round-trips, and a partially-populated row reads as a miss.
/// </summary>
public class PetitionBodyCacheStoreTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();

    public void Dispose() => _dbFactory.Dispose();

    private async Task<Guid> SeedIssueAsync(Action<Issue>? mutate = null)
    {
        var user = TestDataBuilder.CreateUser();
        var issue = TestDataBuilder.CreateIssue(userId: user.Id);
        mutate?.Invoke(issue);
        await using var ctx = _dbFactory.CreateContext();
        ctx.UserProfiles.Add(user);
        ctx.Issues.Add(issue);
        await ctx.SaveChangesAsync();
        return issue.Id;
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsEntry_WithoutTouchingUpdatedAt()
    {
        var issueId = await SeedIssueAsync();

        // The persisted UpdatedAt (post-insert, round-tripped through the provider) is our baseline.
        DateTime updatedAtBefore;
        await using (var ctx = _dbFactory.CreateContext())
        {
            updatedAtBefore = await ctx.Issues.AsNoTracking().Where(i => i.Id == issueId)
                .Select(i => i.UpdatedAt).SingleAsync();
        }

        var generatedAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _dbFactory.CreateContext())
        {
            await new PetitionBodyCacheStore(ctx).SetAsync(issueId, "NUCLEU", "ABCDEF0123", generatedAt);
        }

        await using (var ctx = _dbFactory.CreateContext())
        {
            var entry = await new PetitionBodyCacheStore(ctx).GetAsync(issueId);
            entry.Should().NotBeNull();
            entry!.Core.Should().Be("NUCLEU");
            entry.ContentHash.Should().Be("ABCDEF0123");
            entry.GeneratedAtUtc.Should().BeCloseTo(generatedAt, TimeSpan.FromSeconds(1));

            // The cache write must not look like an issue edit.
            var updatedAtAfter = await ctx.Issues.AsNoTracking().Where(i => i.Id == issueId)
                .Select(i => i.UpdatedAt).SingleAsync();
            updatedAtAfter.Should().Be(updatedAtBefore);
        }
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNothingCached()
    {
        var issueId = await SeedIssueAsync();

        await using var ctx = _dbFactory.CreateContext();
        (await new PetitionBodyCacheStore(ctx).GetAsync(issueId)).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenEntryPartiallyPopulated()
    {
        // Core present but hash/timestamp missing — a half-written row must read as a miss, not a hit.
        var issueId = await SeedIssueAsync(i =>
        {
            i.PetitionBodyCore = "orfan";
            i.PetitionBodyContentHash = null;
            i.PetitionBodyGeneratedAt = null;
        });

        await using var ctx = _dbFactory.CreateContext();
        (await new PetitionBodyCacheStore(ctx).GetAsync(issueId)).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenIssueDoesNotExist()
    {
        await using var ctx = _dbFactory.CreateContext();
        (await new PetitionBodyCacheStore(ctx).GetAsync(Guid.NewGuid())).Should().BeNull();
    }
}
