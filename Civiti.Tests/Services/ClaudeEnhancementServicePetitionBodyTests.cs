using System.Threading.RateLimiting;
using Anthropic.SDK;
using Civiti.Application.Requests.Issues;
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
    private static ClaudeEnhancementService CreateUnconfiguredService()
    {
        var logger = new Mock<ILogger<ClaudeEnhancementService>>().Object;
        var config = new ClaudeConfiguration { ApiKey = string.Empty }; // IsConfigured == false
        var rateLimiter = PartitionedRateLimiter.Create<Guid, Guid>(
            key => RateLimitPartition.GetNoLimiter(key));
        var client = new AnthropicClient("test-unused"); // never called on the fallback path
        return new ClaudeEnhancementService(logger, config, rateLimiter, client);
    }

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
        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
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
        body.Should().Contain("CNP [CNP-UL TĂU]");
        body.Should().Contain("[ADRESA TA DE DOMICILIU]");
        body.Should().Contain("O.G. 27/2002");
        body.Should().Contain("termenul legal de 30 de zile");
        body.Should().Contain("Cu stimă,");
        body.Should().Contain("[NUMELE TĂU COMPLET]");

        // Issue facts + deterministic core.
        body.Should().Contain("Problemă: Groapă periculoasă pe strada Mihai Eminescu");
        body.Should().Contain("Locație: Strada Mihai Eminescu 12, Sector 2");
        body.Should().Contain("Data sesizării: 01.03.2026");
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
}
