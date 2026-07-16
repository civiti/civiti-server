using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Civiti.Infrastructure.Configuration;
using Civiti.Domain.Entities;
using Civiti.Application.Requests.Issues;
using Civiti.Application.Responses.Issues;
using Civiti.Application.Services;

namespace Civiti.Infrastructure.Services.Claude;

/// <summary>
/// Service for enhancing civic issue text using Claude AI
/// </summary>
public class ClaudeEnhancementService(
    ILogger<ClaudeEnhancementService> logger,
    ClaudeConfiguration configuration,
    PartitionedRateLimiter<Guid> rateLimiter,
    AnthropicClient anthropicClient,
    IPetitionBodyCacheStore petitionCache)
    : IClaudeEnhancementService
{
    // Bump when a change to the petition prompt (system/user) should invalidate every cached core.
    // Scaffold-only changes need no bump — the scaffold is re-assembled live, only the AI core is cached.
    private const string PetitionPromptVersion = "v1";

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

    private const string PetitionCoreSystemPrompt = """
        Ești asistentul de redactare al platformei civice Civiti. Sarcina ta: compui DOAR corpul argumentativ al unei petiții adresate unei autorități publice locale din România.

        Reguli:
        - Scrii în limba română, într-un registru formal, dar concis și la obiect.
        - Compui 1–3 paragrafe scurte, legate logic: (1) problema, concret; (2) impactul asupra comunității; (3) ce anume soliciți autorității, clar și ferm.
        - NU incluzi: formula de adresare (ex. „Către"), datele de identificare ale petentului, temeiuri legale (ex. O.G. 27/2002), formule de încheiere sau semnătură — acestea se adaugă automat.
        - NU inventezi fapte, cifre, nume, date sau locații care nu apar în informațiile primite.
        - NU incluzi linkuri, titluri de secțiune, marcaje Markdown sau text în paranteze pătrate.
        - Eviți limbajul birocratic redundant, repetițiile și clișeele. Textul trebuie să curgă natural de la problemă la solicitare.

        Răspunde cu textul corpului, în text simplu, fără alte comentarii.
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
        using RateLimitLease lease = rateLimiter.AttemptAcquire(userId);
        if (!lease.IsAcquired)
        {
            logger.LogWarning("User {UserId} exceeded rate limit", userId);
            return CreateRateLimitedResponse(request);
        }

        try
        {
            var userPrompt = BuildUserPrompt(request);

            MessageParameters messageRequest = new()
            {
                Model = configuration.Model,
                MaxTokens = configuration.MaxTokens,
                System = [new SystemMessage(SystemPrompt)],
                Messages = [new Message(RoleType.User, userPrompt)]
            };

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
            MessageResponse? response = await anthropicClient.Messages.GetClaudeMessageAsync(messageRequest, cts.Token);

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
    public async Task<PetitionBodyResponse> GeneratePetitionBodyAsync(PetitionBodyRequest request, Guid userId)
    {
        var contentHash = ComputePetitionContentHash(request);
        var cachingEnabled = configuration.PetitionCacheTtl > TimeSpan.Zero;

        // Serve a fresh cached core without touching Claude or the rate limiter. The core is
        // user-agnostic, so one entry serves every citizen viewing the issue; the content hash
        // invalidates it on edit, the TTL bounds staleness. Regenerate deliberately bypasses this.
        if (cachingEnabled && !request.Regenerate)
        {
            PetitionCoreCacheEntry? cached = await petitionCache.GetAsync(request.IssueId);
            if (cached is not null
                && cached.ContentHash == contentHash
                && DateTime.UtcNow - cached.GeneratedAtUtc < configuration.PetitionCacheTtl)
            {
                return new PetitionBodyResponse
                {
                    Body = AssemblePetitionBody(cached.Core, request),
                    UsedOriginalText = false
                };
            }
        }

        // Fall back to a deterministic, still-compliant body when Claude is not configured.
        if (!configuration.IsConfigured)
        {
            logger.LogWarning("Claude API key is not configured, returning deterministic petition body");
            return BuildFallbackPetitionResponse(request, "AI composition is not available.");
        }

        using RateLimitLease lease = rateLimiter.AttemptAcquire(userId);
        if (!lease.IsAcquired)
        {
            logger.LogWarning("User {UserId} exceeded rate limit (petition body)", userId);
            return BuildRateLimitedPetitionResponse(request);
        }

        try
        {
            var userPrompt = BuildPetitionUserPrompt(request);

            MessageParameters messageRequest = new()
            {
                Model = configuration.Model,
                MaxTokens = configuration.MaxTokens,
                System = [new SystemMessage(PetitionCoreSystemPrompt)],
                Messages = [new Message(RoleType.User, userPrompt)]
            };

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
            MessageResponse? response = await anthropicClient.Messages.GetClaudeMessageAsync(messageRequest, cts.Token);

            var core = response?.Content?
                .OfType<TextContent>()
                .Select(c => c.Text)
                .FirstOrDefault();

            core = string.IsNullOrWhiteSpace(core) ? null : CleanPetitionCore(core);

            if (string.IsNullOrWhiteSpace(core))
            {
                logger.LogWarning("Empty or invalid petition core from Claude API");
                return BuildFallbackPetitionResponse(request, "AI returned an empty response.");
            }

            // Cache only successful AI cores (never the deterministic fallback), so a transient AI
            // failure never pins a degraded body for the whole TTL. A cache-write failure must not
            // discard the body we already composed — log and serve it anyway.
            if (cachingEnabled)
            {
                try
                {
                    await petitionCache.SetAsync(request.IssueId, core, contentHash, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cache petition core for issue {IssueId}", request.IssueId);
                }
            }

            logger.LogInformation("Successfully composed petition body for issue {IssueId}", request.IssueId);
            return new PetitionBodyResponse
            {
                Body = AssemblePetitionBody(core, request),
                UsedOriginalText = false
            };
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Claude API request timed out for petition body (user {UserId})", userId);
            return BuildFallbackPetitionResponse(request, "AI request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error composing petition body for user {UserId}", userId);
            return BuildFallbackPetitionResponse(request, "An error occurred while composing the petition.");
        }
    }

    /// <inheritdoc />
    public bool IsRateLimited(Guid userId)
    {
        RateLimiterStatistics? statistics = rateLimiter.GetStatistics(userId);
        return statistics?.CurrentAvailablePermits == 0;
    }

    /// <summary>
    /// SHA-256 fingerprint of the fields that feed the AI core (prompt version + the exact inputs
    /// of <see cref="BuildPetitionUserPrompt"/>). Excludes IssueId/PhotoCount (scaffold-only, so a
    /// new photo reuses the core) and Regenerate (a bypass flag). An edit to any prompt-affecting
    /// field yields a new hash and thus a fresh generation. <c>internal</c> so tests can reproduce it.
    /// </summary>
    internal static string ComputePetitionContentHash(PetitionBodyRequest request)
    {
        // Unit-separator delimiter avoids field-boundary collisions (e.g. "ab"+"c" vs "a"+"bc").
        var canonical = string.Join('\u001f',
            PetitionPromptVersion,
            request.Title ?? string.Empty,
            request.Category.ToString(),
            request.Address ?? string.Empty,
            request.District ?? string.Empty,
            request.Description ?? string.Empty,
            request.DesiredOutcome ?? string.Empty,
            request.CommunityImpact ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string BuildPetitionUserPrompt(PetitionBodyRequest request)
    {
        var categoryName = GetCategoryName(request.Category);
        var location = FormatLocation(request.Address, request.District);

        List<string> sections =
        [
            "Compune corpul petiției pe baza următoarelor informații:",
            $"Titlu: {request.Title}",
            $"Categorie: {categoryName}",
            $"Locație: {location}",
            $"Descrierea problemei: {request.Description}"
        ];

        if (!string.IsNullOrWhiteSpace(request.DesiredOutcome))
        {
            sections.Add($"Rezultatul dorit: {request.DesiredOutcome}");
        }

        if (!string.IsNullOrWhiteSpace(request.CommunityImpact))
        {
            sections.Add($"Impact asupra comunității: {request.CommunityImpact}");
        }

        if (request.Regenerate)
        {
            sections.Add("Oferă o formulare distinctă față de variantele uzuale, păstrând aceleași fapte.");
        }

        return string.Join("\n", sections);
    }

    /// <summary>
    /// Strip stray Markdown the model was told not to emit (code fences, bold/italic emphasis,
    /// inline code, and heading markers) so it never leaks into the petition, then trim. The
    /// scaffold — not the model — owns links, placeholders, and legal text. The emphasis patterns
    /// only match markers hugging non-space text (e.g. "*urgent*"), so stray asterisks in prose
    /// (e.g. "A * B") are left untouched.
    /// </summary>
    private static string CleanPetitionCore(string text)
    {
        var cleaned = text
            .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty)
            .Replace("`", string.Empty);

        cleaned = Regex.Replace(cleaned, @"\*\*(\S(?:[^*\n]*\S)?)\*\*", "$1"); // **bold**
        cleaned = Regex.Replace(cleaned, @"\*(\S(?:[^*\n]*\S)?)\*", "$1");     // *italic*
        cleaned = Regex.Replace(cleaned, @"(?m)^\s{0,3}#{1,6}\s+", string.Empty); // # heading

        return cleaned.Trim();
    }

    /// <summary>
    /// Deterministically wrap the (AI or fallback) argument core in the legally-compliant
    /// O.G. 27/2002 scaffold with bracketed PII placeholders the citizen fills in their client.
    /// Assembled in code so required elements can never be dropped by the model.
    /// </summary>
    private static string AssemblePetitionBody(string core, PetitionBodyRequest request)
    {
        var location = FormatLocation(request.Address, request.District);
        var photosLine = FormatPhotosLine(request.PhotoCount);

        var factsBlock = string.Join('\n',
            $"Problemă: {request.Title}",
            $"Locație: {location}");

        var documentationBlock = $"{photosLine}Documentație completă: https://civiti.ro/issues/{request.IssueId}";

        var signoff = string.Join('\n',
            "Cu stimă,",
            "[NUMELE TĂU COMPLET]",
            "Telefon: [NUMĂRUL TĂU DE TELEFON]");

        return string.Join("\n\n",
            "Către: [NUMELE AUTORITĂȚII]",
            "Subsemnatul/a [NUMELE TĂU COMPLET], cu domiciliul în [ADRESA TA DE DOMICILIU], vă adresez următoarea petiție:",
            factsBlock,
            core,
            documentationBlock,
            "Conform O.G. 27/2002, vă rog să îmi comunicați numărul de înregistrare al petiției și răspunsul în termenul legal de 30 de zile.",
            signoff);
    }

    /// <summary>
    /// Deterministic argument core used when AI composition is unavailable — mirrors the
    /// legacy client-side template middle (raw description + community impact + desired outcome).
    /// </summary>
    private static string BuildFallbackCore(PetitionBodyRequest request)
    {
        List<string> parts = [];

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            parts.Add(request.Description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.CommunityImpact))
        {
            parts.Add(request.CommunityImpact.Trim());
        }

        parts.Add(!string.IsNullOrWhiteSpace(request.DesiredOutcome)
            ? request.DesiredOutcome.Trim()
            : "Vă solicit să luați măsurile necesare pentru remedierea acestei probleme în cel mai scurt timp posibil.");

        return string.Join("\n\n", parts);
    }

    private static string FormatLocation(string address, string? district)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(address))
        {
            parts.Add(address.Trim());
        }
        if (!string.IsNullOrWhiteSpace(district))
        {
            parts.Add(district.Trim());
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "Locație nespecificată";
    }

    private static string FormatPhotosLine(int photoCount)
    {
        if (photoCount <= 0)
        {
            return string.Empty;
        }
        var noun = photoCount == 1 ? "fotografie care documentează" : "fotografii care documentează";
        return $"La prezenta petiție anexez {photoCount} {noun} problema semnalată.\n";
    }

    private static PetitionBodyResponse BuildFallbackPetitionResponse(PetitionBodyRequest request, string warning) =>
        new()
        {
            Body = AssemblePetitionBody(BuildFallbackCore(request), request),
            UsedOriginalText = true,
            Warning = warning
        };

    private static PetitionBodyResponse BuildRateLimitedPetitionResponse(PetitionBodyRequest request) =>
        new()
        {
            Body = AssemblePetitionBody(BuildFallbackCore(request), request),
            UsedOriginalText = true,
            Warning = "Rate limit exceeded. Please try again later.",
            IsRateLimited = true
        };

    private static string BuildUserPrompt(EnhanceTextRequest request)
    {
        var categoryName = GetCategoryName(request.Category);
        var hasDesiredOutcome = !string.IsNullOrWhiteSpace(request.DesiredOutcome);
        var hasCommunityImpact = !string.IsNullOrWhiteSpace(request.CommunityImpact);

        List<string> sections = [$"Categorie problemă: {categoryName}"];

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
        List<string> jsonFields = ["\"enhancedDescription\": \"descrierea îmbunătățită aici\""];
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

            using JsonDocument jsonDoc = JsonDocument.Parse(cleanedResponse);
            JsonElement root = jsonDoc.RootElement;

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
        return element.TryGetProperty(propertyName, out JsonElement prop) ? prop.GetString() : null;
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
