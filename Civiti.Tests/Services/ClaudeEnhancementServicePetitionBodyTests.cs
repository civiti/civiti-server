using System.Threading.RateLimiting;
using Anthropic.SDK;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Infrastructure.Configuration;
using Civiti.Infrastructure.Services.Claude;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Civiti.Tests.Services;

/// <summary>
/// Deterministic coverage for the petition-body scaffold. These run the AI-unavailable path
/// (empty API key → <see cref="ClaudeConfiguration.IsConfigured"/> is false), which composes
/// the full body from the deterministic fallback core with no network call. The assertions pin
/// the legally load-bearing structure (O.G. 27/2002 reply clause, identity placeholders) that
/// must be present regardless of what the model returns.
/// </summary>
public class ClaudeEnhancementServicePetitionBodyTests
{
    private static ClaudeEnhancementService CreateUnconfiguredService(
        IPetitionBodyCacheStore? cache = null,
        int cacheHours = ClaudeConfiguration.DefaultPetitionCacheHours)
    {
        var logger = new Mock<ILogger<ClaudeEnhancementService>>().Object;
        // IsConfigured == false, so the AI path is never taken — but the cache is checked *before*
        // IsConfigured, so a seeded fresh entry is still served, which is what the cache tests use.
        var config = new ClaudeConfiguration { ApiKey = string.Empty, PetitionCacheHours = cacheHours };
        var rateLimiter = PartitionedRateLimiter.Create<Guid, Guid>(
            key => RateLimitPartition.GetNoLimiter(key));
        var client = new AnthropicClient("test-unused"); // never called on the fallback path
        return new ClaudeEnhancementService(logger, config, rateLimiter, client, cache ?? new FakePetitionCache());
    }

    /// <summary>In-memory <see cref="IPetitionBodyCacheStore"/> so cache behaviour is testable without a DB.</summary>
    private sealed class FakePetitionCache : IPetitionBodyCacheStore
    {
        private readonly Dictionary<Guid, PetitionCoreCacheEntry> _store = [];

        public Task<PetitionCoreCacheEntry?> GetAsync(Guid issueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(issueId, out var entry) ? entry : null);

        public Task SetAsync(Guid issueId, string core, string contentHash, DateTime generatedAtUtc, CancellationToken cancellationToken = default)
        {
            _store[issueId] = new PetitionCoreCacheEntry(core, contentHash, generatedAtUtc);
            return Task.CompletedTask;
        }

        public void Seed(Guid issueId, PetitionCoreCacheEntry entry) => _store[issueId] = entry;
    }

    /// <summary>
    /// Configured service whose AI call is stubbed via the RequestPetitionCoreAsync seam, so the
    /// AI-success write path (SetAsync) can be exercised deterministically with no live API call.
    /// A null core simulates an empty/invalid AI response.
    /// </summary>
    private sealed class StubClaudeService : ClaudeEnhancementService
    {
        private readonly string? _core;

        public StubClaudeService(ClaudeConfiguration config, IPetitionBodyCacheStore cache, string? core)
            : base(new Mock<ILogger<ClaudeEnhancementService>>().Object, config,
                   PartitionedRateLimiter.Create<Guid, Guid>(key => RateLimitPartition.GetNoLimiter(key)),
                   new AnthropicClient("test-unused"), cache)
        {
            _core = core;
        }

        protected override Task<string?> RequestPetitionCoreAsync(PetitionBodyRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(_core);
    }

    private static StubClaudeService CreateConfiguredService(IPetitionBodyCacheStore cache, string? aiCore) =>
        new(new ClaudeConfiguration { ApiKey = "sk-test", PetitionCacheHours = ClaudeConfiguration.DefaultPetitionCacheHours },
            cache, aiCore);

    private static PetitionBodyRequest SampleRequest(int photoCount = 2) => new()
    {
        IssueId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Title = "Groapă periculoasă pe strada Mihai Eminescu",
        Category = IssueCategory.Infrastructure,
        Address = "Strada Mihai Eminescu 12",
        District = "Sector 2",
        Description = "Există o groapă mare care pune în pericol mașinile.",
        DesiredOutcome = "Repararea urgentă a carosabilului.",
        CommunityImpact = "Zeci de locuitori trec zilnic pe aici.",
        PhotoCount = photoCount,
        Regenerate = false
    };

    [Fact]
    public async Task GeneratePetitionBodyAsync_WhenClaudeNotConfigured_ReturnsCompliantDeterministicBody()
    {
        var service = CreateUnconfiguredService();

        var response = await service.GeneratePetitionBodyAsync(SampleRequest(), Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        response.IsRateLimited.Should().BeFalse();
        response.Warning.Should().NotBeNullOrEmpty();

        var body = response.Body;

        // Legally load-bearing scaffold — must always be present.
        body.Should().Contain("Către: [NUMELE AUTORITĂȚII]");
        body.Should().NotContain("CNP");
        body.Should().Contain("[ADRESA TA DE DOMICILIU]");
        body.Should().Contain("O.G. 27/2002");
        body.Should().Contain("termenul legal de 30 de zile");
        body.Should().Contain("Cu stimă,");
        body.Should().Contain("[NUMELE TĂU COMPLET]");
        body.Should().EndWith("Telefon: [NUMĂRUL TĂU DE TELEFON]");

        // Issue facts + deterministic core.
        body.Should().Contain("Problemă: Groapă periculoasă pe strada Mihai Eminescu");
        body.Should().Contain("Locație: Strada Mihai Eminescu 12, Sector 2");
        body.Should().Contain("Există o groapă mare");
        body.Should().Contain("Zeci de locuitori trec zilnic pe aici.");
        body.Should().Contain("Repararea urgentă a carosabilului.");

        // Documentation link + photos annotation.
        body.Should().Contain("https://civiti.ro/issues/11111111-1111-1111-1111-111111111111");
        body.Should().Contain("anexez 2 fotografii care documentează problema semnalată.");
    }

    [Theory]
    [InlineData(1, "anexez 1 fotografie care documentează")]
    [InlineData(3, "anexez 3 fotografii care documentează")]
    public async Task GeneratePetitionBodyAsync_PhotoLine_UsesCorrectPluralization(int photoCount, string expected)
    {
        var service = CreateUnconfiguredService();

        var response = await service.GeneratePetitionBodyAsync(SampleRequest(photoCount), Guid.NewGuid());

        response.Body.Should().Contain(expected);
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WithNoPhotos_OmitsPhotoLine()
    {
        var service = CreateUnconfiguredService();

        var response = await service.GeneratePetitionBodyAsync(SampleRequest(0), Guid.NewGuid());

        response.Body.Should().NotContain("anexez");
        response.Body.Should().Contain("Documentație completă:");
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WithoutDesiredOutcome_UsesGenericDemandFallback()
    {
        var service = CreateUnconfiguredService();
        var request = SampleRequest();
        request.DesiredOutcome = null;

        var response = await service.GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.Body.Should().Contain("Vă solicit să luați măsurile necesare");
    }

    // The cache is consulted before the IsConfigured/rate-limit checks, so an unconfigured service
    // still serves a seeded fresh entry — that's the seam these tests exercise (no live Claude call).
    private const string CachedCore = "NUCLEU CACHED DISTINCTIV pentru testul de cache.";
    private const string FallbackMarker = "Există o groapă mare"; // appears only in the deterministic core

    [Fact]
    public async Task GeneratePetitionBodyAsync_WithFreshCachedCore_ServesCacheWithoutRegenerating()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();
        var hash = ClaudeEnhancementService.ComputePetitionContentHash(request);
        cache.Seed(request.IssueId, new PetitionCoreCacheEntry(CachedCore, hash, DateTime.UtcNow));
        var service = CreateUnconfiguredService(cache);

        var response = await service.GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeFalse();
        response.Warning.Should().BeNull();
        response.Body.Should().Contain(CachedCore);
        response.Body.Should().NotContain(FallbackMarker); // proves the fallback core was not used
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WithStaleCachedCore_IgnoresCacheAndFallsBack()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();
        var hash = ClaudeEnhancementService.ComputePetitionContentHash(request);
        // Older than the default 24h TTL.
        cache.Seed(request.IssueId, new PetitionCoreCacheEntry(CachedCore, hash, DateTime.UtcNow.AddHours(-25)));
        var service = CreateUnconfiguredService(cache);

        var response = await service.GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        response.Body.Should().NotContain(CachedCore);
        response.Body.Should().Contain(FallbackMarker);
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WithMismatchedHash_IgnoresCacheAndFallsBack()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();
        // A stale fingerprint, as if the issue text changed after the core was cached.
        cache.Seed(request.IssueId, new PetitionCoreCacheEntry(CachedCore, "0000deadbeef", DateTime.UtcNow));
        var service = CreateUnconfiguredService(cache);

        var response = await service.GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        response.Body.Should().NotContain(CachedCore);
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WhenRegenerate_BypassesFreshCache()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();
        request.Regenerate = true;
        // Hash excludes Regenerate, so this entry *would* match — proving the bypass is deliberate.
        var hash = ClaudeEnhancementService.ComputePetitionContentHash(request);
        cache.Seed(request.IssueId, new PetitionCoreCacheEntry(CachedCore, hash, DateTime.UtcNow));
        var service = CreateUnconfiguredService(cache);

        var response = await service.GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        response.Body.Should().NotContain(CachedCore);
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WhenCachingDisabled_IgnoresFreshCache()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();
        var hash = ClaudeEnhancementService.ComputePetitionContentHash(request);
        cache.Seed(request.IssueId, new PetitionCoreCacheEntry(CachedCore, hash, DateTime.UtcNow));
        var service = CreateUnconfiguredService(cache, cacheHours: 0); // TTL <= 0 disables caching

        var response = await service.GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        response.Body.Should().NotContain(CachedCore);
    }

    // ── Write path: a configured service (AI call stubbed via the seam) must persist the core ──

    [Fact]
    public async Task GeneratePetitionBodyAsync_OnAiSuccess_CachesCoreUnderContentHashAndReserves()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();
        const string aiCore = "NUCLEU COMPUS DE AI, distinct de textul de fallback.";

        var response = await CreateConfiguredService(cache, aiCore).GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeFalse();
        response.Body.Should().Contain(aiCore);

        // Written under the EXACT hash the read path later recomputes (the invariant the design relies on).
        var stored = await cache.GetAsync(request.IssueId);
        stored.Should().NotBeNull();
        stored!.Core.Should().Be(aiCore);
        stored.ContentHash.Should().Be(ClaudeEnhancementService.ComputePetitionContentHash(request));

        // A second service that would compose a DIFFERENT core still serves the cached one.
        var second = await CreateConfiguredService(cache, "AR TREBUI IGNORAT").GeneratePetitionBodyAsync(request, Guid.NewGuid());
        second.UsedOriginalText.Should().BeFalse();
        second.Body.Should().Contain(aiCore);
        second.Body.Should().NotContain("AR TREBUI IGNORAT");
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_OnEmptyAiCore_FallsBackAndDoesNotCache()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();

        var response = await CreateConfiguredService(cache, aiCore: null).GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        (await cache.GetAsync(request.IssueId)).Should().BeNull(); // the deterministic fallback is never cached
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WhenRegenerate_OverwritesCachedCore()
    {
        var cache = new FakePetitionCache();
        var request = SampleRequest();
        var hash = ClaudeEnhancementService.ComputePetitionContentHash(request);
        cache.Seed(request.IssueId, new PetitionCoreCacheEntry("NUCLEU VECHI", hash, DateTime.UtcNow));
        request.Regenerate = true;

        var response = await CreateConfiguredService(cache, "NUCLEU REGENERAT").GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeFalse();
        response.Body.Should().Contain("NUCLEU REGENERAT");
        (await cache.GetAsync(request.IssueId))!.Core.Should().Be("NUCLEU REGENERAT");
    }

    // ── Content-hash contract: pin the value and the field inclusion/exclusion set ──

    private static PetitionBodyRequest GoldenRequest() => new()
    {
        IssueId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Title = "Titlu fix",
        Category = IssueCategory.Infrastructure,
        Address = "Strada Fixa 1",
        District = "Sector 1",
        Description = "Descriere fixa.",
        DesiredOutcome = "Rezultat fix.",
        CommunityImpact = "Impact fix.",
        PhotoCount = 3,
        Regenerate = false
    };

    private static string HashOf(Action<PetitionBodyRequest> mutate)
    {
        var request = GoldenRequest();
        mutate(request);
        return ClaudeEnhancementService.ComputePetitionContentHash(request);
    }

    [Fact]
    public void ComputePetitionContentHash_IsDeterministic_AndMatchesGoldenValue()
    {
        var hash = ClaudeEnhancementService.ComputePetitionContentHash(GoldenRequest());

        hash.Should().MatchRegex("^[0-9A-F]{64}$");
        hash.Should().Be(ClaudeEnhancementService.ComputePetitionContentHash(GoldenRequest()));
        // Golden digest: if this breaks, the prompt version / field set / delimiter changed and EVERY
        // cached core in production will stop matching on the next read — bump deliberately, not by accident.
        hash.Should().Be("13CDC58392CFECF0FC68B6DD5CAE27DDDD28057C0D368657B4AC264673C1CC90");
    }

    [Fact]
    public void ComputePetitionContentHash_ChangesWithPromptFields_ButNotScaffoldOnlyFields()
    {
        var baseHash = ClaudeEnhancementService.ComputePetitionContentHash(GoldenRequest());

        // Prompt-affecting fields MUST change the hash (an edit invalidates the cache).
        HashOf(r => r.Title += "x").Should().NotBe(baseHash);
        HashOf(r => r.Category = IssueCategory.Environment).Should().NotBe(baseHash);
        HashOf(r => r.Address += "x").Should().NotBe(baseHash);
        HashOf(r => r.District += "x").Should().NotBe(baseHash);
        HashOf(r => r.Description += "x").Should().NotBe(baseHash);
        HashOf(r => r.DesiredOutcome += "x").Should().NotBe(baseHash);
        HashOf(r => r.CommunityImpact += "x").Should().NotBe(baseHash);

        // Scaffold-only / bypass fields MUST NOT change the hash (a new photo reuses the core).
        HashOf(r => r.PhotoCount = 99).Should().Be(baseHash);
        HashOf(r => r.IssueId = Guid.NewGuid()).Should().Be(baseHash);
        HashOf(r => r.Regenerate = true).Should().Be(baseHash);
    }
}
