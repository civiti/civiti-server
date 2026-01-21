using System.Text.Json;
using System.Threading.RateLimiting;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Civica.Api.Infrastructure.Configuration;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Services.Interfaces;

namespace Civica.Api.Services;

/// <summary>
/// Service for enhancing civic issue text using Claude AI
/// </summary>
public class ClaudeEnhancementService(
    ILogger<ClaudeEnhancementService> logger,
    ClaudeConfiguration configuration,
    PartitionedRateLimiter<Guid, string> rateLimiter)
    : IClaudeEnhancementService
{
    private const string SystemPrompt = """
        Ești un asistent specializat în îmbunătățirea textelor pentru sesizări civice în România.

        Rolul tău este să transformi descrieri informale sau incomplete ale problemelor civice în texte:
        - Clare și bine structurate
        - Profesionale dar accesibile
        - Cu detalii concrete și specifice
        - Într-un ton respectuos și constructiv
        - Păstrând toate informațiile originale

        NU inventa informații noi. NU schimba locațiile sau datele menționate.
        Îmbunătățește DOAR modul de exprimare, păstrând sensul original.

        Răspunde ÎNTOTDEAUNA în formatul JSON specificat, fără text suplimentar.
        """;

    /// <inheritdoc />
    public async Task<EnhanceTextResponse> EnhanceTextAsync(EnhanceTextRequest request, Guid userId)
    {
        // Check if Claude is configured before consuming rate limit
        if (!configuration.IsConfigured)
        {
            logger.LogWarning("Claude API key is not configured, returning original text");
            return CreateFallbackResponse(request, "AI enhancement is not available.");
        }

        // Use built-in rate limiter with sliding window algorithm
        using var lease = rateLimiter.AttemptAcquire(userId);
        if (!lease.IsAcquired)
        {
            logger.LogWarning("User {UserId} exceeded rate limit", userId);
            return CreateRateLimitedResponse(request);
        }

        try
        {
            using var client = new AnthropicClient(configuration.ApiKey);

            var userPrompt = BuildUserPrompt(request);

            var messageRequest = new MessageParameters
            {
                Model = configuration.Model,
                MaxTokens = configuration.MaxTokens,
                System = [new SystemMessage(SystemPrompt)],
                Messages = [new Message(RoleType.User, userPrompt)]
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
            var response = await client.Messages.GetClaudeMessageAsync(messageRequest, cts.Token);

            if (response?.Content == null || response.Content.Count == 0)
            {
                logger.LogWarning("Empty response from Claude API");
                return CreateFallbackResponse(request, "AI returned an empty response.");
            }

            var responseText = response.Content
                .OfType<TextContent>()
                .Select(c => c.Text)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(responseText))
            {
                logger.LogWarning("No text content in Claude response");
                return CreateFallbackResponse(request, "AI returned an invalid response.");
            }

            return ParseClaudeResponse(responseText, request);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Claude API request timed out for user {UserId}", userId);
            return CreateFallbackResponse(request, "AI request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling Claude API for user {UserId}", userId);
            return CreateFallbackResponse(request, "An error occurred while enhancing text.");
        }
    }

    /// <inheritdoc />
    public bool IsRateLimited(Guid userId)
    {
        var statistics = rateLimiter.GetStatistics(userId);
        return statistics?.CurrentAvailablePermits == 0;
    }

    private static string BuildUserPrompt(EnhanceTextRequest request)
    {
        var categoryName = GetCategoryName(request.Category);
        var hasDesiredOutcome = !string.IsNullOrWhiteSpace(request.DesiredOutcome);
        var hasCommunityImpact = !string.IsNullOrWhiteSpace(request.CommunityImpact);

        var sections = new List<string>
        {
            $"Categorie problemă: {categoryName}"
        };

        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            sections.Add($"Locație: {request.Location}");
        }

        sections.Add($"""

            Descrierea originală a cetățeanului:
            "{request.Description}"
            """);

        if (hasDesiredOutcome)
        {
            sections.Add($"""
                Rezultatul dorit de cetățean:
                "{request.DesiredOutcome}"
                """);
        }

        if (hasCommunityImpact)
        {
            sections.Add($"""
                Impactul asupra comunității:
                "{request.CommunityImpact}"
                """);
        }

        sections.Add("Îmbunătățește textul/textele de mai sus păstrând toate informațiile originale.");

        // Build JSON format specification
        var jsonFields = new List<string> { "\"enhancedDescription\": \"descrierea îmbunătățită aici\"" };
        if (hasDesiredOutcome)
        {
            jsonFields.Add("\"enhancedDesiredOutcome\": \"rezultatul dorit îmbunătățit aici\"");
        }
        if (hasCommunityImpact)
        {
            jsonFields.Add("\"enhancedCommunityImpact\": \"impactul asupra comunității îmbunătățit aici\"");
        }

        var jsonFormat = "{\n    " + string.Join(",\n    ", jsonFields) + "\n}";
        sections.Add($"Răspunde STRICT în următorul format JSON (fără markdown code blocks):\n{jsonFormat}");

        return string.Join("\n\n", sections);
    }

    private static string GetCategoryName(IssueCategory category) => category switch
    {
        IssueCategory.Infrastructure => "Infrastructură (drumuri, trotuare, poduri)",
        IssueCategory.Environment => "Mediu (parcuri, poluare, deșeuri)",
        IssueCategory.Transportation => "Transport (transport public, trafic)",
        IssueCategory.PublicServices => "Servicii publice (utilități, servicii guvernamentale)",
        IssueCategory.Safety => "Siguranță (iluminat, pericole)",
        IssueCategory.Other => "Altele",
        _ => "General"
    };

    private EnhanceTextResponse ParseClaudeResponse(string responseText, EnhanceTextRequest request)
    {
        try
        {
            var cleanedResponse = responseText
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            using var jsonDoc = JsonDocument.Parse(cleanedResponse);
            var root = jsonDoc.RootElement;

            var enhancedDescription = GetJsonProperty(root, "enhancedDescription");
            if (string.IsNullOrEmpty(enhancedDescription))
            {
                return new EnhanceTextResponse
                {
                    EnhancedDescription = request.Description,
                    EnhancedDesiredOutcome = request.DesiredOutcome,
                    EnhancedCommunityImpact = request.CommunityImpact,
                    UsedOriginalText = true,
                    Warning = "Could not parse enhanced description."
                };
            }

            logger.LogInformation("Successfully enhanced text for civic issue");

            return new EnhanceTextResponse
            {
                EnhancedDescription = enhancedDescription,
                EnhancedDesiredOutcome = !string.IsNullOrWhiteSpace(request.DesiredOutcome)
                    ? GetJsonProperty(root, "enhancedDesiredOutcome") ?? request.DesiredOutcome
                    : null,
                EnhancedCommunityImpact = !string.IsNullOrWhiteSpace(request.CommunityImpact)
                    ? GetJsonProperty(root, "enhancedCommunityImpact") ?? request.CommunityImpact
                    : null,
                UsedOriginalText = false
            };
        }
        catch (JsonException ex)
        {
            // Truncate response to avoid logging sensitive user content
            var truncatedResponse = responseText.Length > 100
                ? responseText[..100] + "..."
                : responseText;
            logger.LogWarning(ex, "Failed to parse Claude response as JSON (truncated): {Response}", truncatedResponse);
            return CreateFallbackResponse(request, "Could not parse AI response.");
        }
    }

    private static string? GetJsonProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }

    private static EnhanceTextResponse CreateFallbackResponse(EnhanceTextRequest request, string warning)
    {
        return new EnhanceTextResponse
        {
            EnhancedDescription = request.Description,
            EnhancedDesiredOutcome = request.DesiredOutcome,
            EnhancedCommunityImpact = request.CommunityImpact,
            UsedOriginalText = true,
            Warning = warning
        };
    }

    private static EnhanceTextResponse CreateRateLimitedResponse(EnhanceTextRequest request)
    {
        return new EnhanceTextResponse
        {
            EnhancedDescription = request.Description,
            EnhancedDesiredOutcome = request.DesiredOutcome,
            EnhancedCommunityImpact = request.CommunityImpact,
            UsedOriginalText = true,
            Warning = "Rate limit exceeded. Please try again later.",
            IsRateLimited = true
        };
    }
}
