using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
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
/// Pins the Claude request shape and response handling. The real <see cref="AnthropicClient"/> runs
/// against a fake HTTP handler, so these cover what goes over the wire (model, effort, no thinking
/// or sampling fields) and how stop reasons are treated, without a live API call. Assertions are
/// written against the <see cref="ClaudeConfiguration"/> constants, not literals, so changing the
/// model doesn't require editing these tests.
/// </summary>
public class ClaudeEnhancementServiceRequestTests
{
    private const string ThinkingThenText = """[{"type":"thinking","thinking":"","signature":"sig"},{"type":"text","text":"__TEXT__"}]""";

    private sealed class FakeAnthropicHandler(string stopReason, string text) : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var content = ThinkingThenText.Replace("__TEXT__", JsonEncodedText.Encode(text).ToString());
            var json = $$$"""{"id":"msg_test","type":"message","role":"assistant","model":"{{{ClaudeConfiguration.Model}}}","content":{{{content}}},"stop_reason":"{{{stopReason}}}","usage":{"input_tokens":10,"output_tokens":10}}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static ClaudeEnhancementService CreateService(FakeAnthropicHandler handler, IPetitionBodyCacheStore? cache = null) =>
        new(new Mock<ILogger<ClaudeEnhancementService>>().Object,
            new ClaudeConfiguration { ApiKey = "sk-test" },
            PartitionedRateLimiter.Create<Guid, Guid>(key => RateLimitPartition.GetNoLimiter(key)),
            new AnthropicClient(new APIAuthentication("sk-test"), new HttpClient(handler)),
            cache ?? new Mock<IPetitionBodyCacheStore>().Object);

    private static EnhanceTextRequest SampleEnhanceRequest() => new()
    {
        Description = "groapa mare pe strada, periculos pentru masini",
        Category = IssueCategory.Infrastructure,
        Location = "Strada Mihai Eminescu, Sector 2"
    };

    private static PetitionBodyRequest SamplePetitionRequest() => new()
    {
        IssueId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Title = "Groapă periculoasă",
        Category = IssueCategory.Infrastructure,
        Address = "Strada Mihai Eminescu 12",
        District = "Sector 2",
        Description = "Există o groapă mare care pune în pericol mașinile.",
        PhotoCount = 0
    };

    [Fact]
    public void BuildMessageParameters_UsesConfiguredModelAndEffort_AndSendsNoThinkingOrSampling()
    {
        var parameters = ClaudeEnhancementService.BuildMessageParameters("sys", "user", 1234);

        parameters.Model.Should().Be(ClaudeConfiguration.Model);
        parameters.MaxTokens.Should().Be(1234);
        parameters.OutputConfig.Should().NotBeNull();
        parameters.OutputConfig!.Effort.Should().Be(ClaudeConfiguration.Effort);
        // Unset thinking = the model's default (adaptive). The SDK would serialize an empty
        // ThinkingParameters as {"type":"enabled"}, which current models reject.
        parameters.Thinking.Should().BeNull();
        parameters.Temperature.Should().BeNull();
        parameters.TopP.Should().BeNull();
        parameters.TopK.Should().BeNull();
    }

    [Fact]
    public async Task EnhanceTextAsync_SendsModelAndEffortOnTheWire_AndReadsTextAfterThinkingBlock()
    {
        var handler = new FakeAnthropicHandler("end_turn", """{"enhancedDescription":"Text îmbunătățit."}""");
        var service = CreateService(handler);

        var response = await service.EnhanceTextAsync(SampleEnhanceRequest(), Guid.NewGuid());

        response.UsedOriginalText.Should().BeFalse();
        response.EnhancedDescription.Should().Be("Text îmbunătățit.");

        handler.RequestBodies.Should().ContainSingle();
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        body.RootElement.GetProperty("model").GetString().Should().Be(ClaudeConfiguration.Model);
        body.RootElement.GetProperty("output_config").GetProperty("effort").GetString()
            .Should().Be(ClaudeConfiguration.Effort.ToString());
        body.RootElement.TryGetProperty("thinking", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("temperature", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public async Task EnhanceTextAsync_WhenReplyIsNotComplete_ReturnsOriginalText(string stopReason)
    {
        // Valid JSON on purpose: the stop reason alone must be enough to reject it.
        var handler = new FakeAnthropicHandler(stopReason, """{"enhancedDescription":"Nu trebuie folosit."}""");
        var service = CreateService(handler);
        var request = SampleEnhanceRequest();

        var response = await service.EnhanceTextAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        response.EnhancedDescription.Should().Be(request.Description);
        response.Warning.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public async Task GeneratePetitionBodyAsync_WhenReplyIsNotComplete_FallsBackAndDoesNotCache(string stopReason)
    {
        const string partialCore = "NUCLEU PARTIAL care nu trebuie servit";
        var handler = new FakeAnthropicHandler(stopReason, partialCore);
        var cache = new Mock<IPetitionBodyCacheStore>();
        var service = CreateService(handler, cache.Object);

        var response = await service.GeneratePetitionBodyAsync(SamplePetitionRequest(), Guid.NewGuid());

        response.UsedOriginalText.Should().BeTrue();
        response.Body.Should().NotContain(partialCore);
        cache.Verify(c => c.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GeneratePetitionBodyAsync_WhenReplyCompletes_UsesAndCachesCore()
    {
        const string core = "Vă sesizez existența unei gropi periculoase.";
        var handler = new FakeAnthropicHandler("end_turn", core);
        var cache = new Mock<IPetitionBodyCacheStore>();
        var service = CreateService(handler, cache.Object);
        var request = SamplePetitionRequest();

        var response = await service.GeneratePetitionBodyAsync(request, Guid.NewGuid());

        response.UsedOriginalText.Should().BeFalse();
        response.Body.Should().Contain(core);
        cache.Verify(c => c.SetAsync(request.IssueId, core,
            ClaudeEnhancementService.ComputePetitionContentHash(request),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ReadCompletedText_JoinsTextBlocksAndSkipsThinking()
    {
        var service = CreateService(new FakeAnthropicHandler("end_turn", "unused"));
        var response = new MessageResponse
        {
            StopReason = "end_turn",
            Content = [new ThinkingContent(), new TextContent { Text = "Prima parte. " }, new TextContent { Text = "A doua parte." }]
        };

        service.ReadCompletedText(response, "test").Should().Be("Prima parte. A doua parte.");
    }

    [Fact]
    public void ReadCompletedText_ReturnsNullForMissingResponseOrNoText()
    {
        var service = CreateService(new FakeAnthropicHandler("end_turn", "unused"));

        service.ReadCompletedText(null, "test").Should().BeNull();
        service.ReadCompletedText(new MessageResponse { StopReason = "end_turn", Content = [new ThinkingContent()] }, "test")
            .Should().BeNull();
    }

    [Fact]
    public void ComputePetitionContentHash_ChangesWithModel()
    {
        var request = SamplePetitionRequest();

        ClaudeEnhancementService.ComputePetitionContentHash(request)
            .Should().Be(ClaudeEnhancementService.ComputePetitionContentHash(request, ClaudeConfiguration.Model));
        ClaudeEnhancementService.ComputePetitionContentHash(request, "model-a")
            .Should().NotBe(ClaudeEnhancementService.ComputePetitionContentHash(request, "model-b"));
    }
}
