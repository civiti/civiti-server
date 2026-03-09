using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Api.Models.Push;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Api.Services;

/// <summary>
/// Background service that drains the push notification channel, resolves user tokens,
/// batches them, and sends via the Expo Push API. Mirrors EmailSenderBackgroundService.
/// </summary>
public class PushNotificationSenderBackgroundService(
    ChannelReader<PushNotificationMessage> channelReader,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ExpoPushConfiguration config,
    ILogger<PushNotificationSenderBackgroundService> logger) : BackgroundService
{
    private const string ExpoPushUrl = "https://exp.host/--/api/v2/push/send";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Push notification sender background service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (PushNotificationMessage message in channelReader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await ProcessMessageAsync(message, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to process push notification for user {UserId}", message.UserId);
                    }
                }

                // ReadAllAsync returned normally — channel is complete, all messages processed.
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Push notification sender crashed — restarting in 5 seconds");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("Push notification sender background service stopping");
    }

    private async Task ProcessMessageAsync(PushNotificationMessage message, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CivitiDbContext>();

        // Re-check preference at delivery time (user may have toggled since enqueue)
        if (!message.ForceSend)
        {
            bool enabled = await context.UserProfiles
                .Where(up => up.Id == message.UserId)
                .Select(up => up.PushNotificationsEnabled)
                .FirstOrDefaultAsync(ct);

            if (!enabled) return;
        }

        // Resolve user's push tokens
        List<string> tokens = await context.PushTokens
            .Where(pt => pt.UserId == message.UserId)
            .Select(pt => pt.Token)
            .ToListAsync(ct);

        if (tokens.Count == 0) return;

        // Build Expo payloads
        var payloads = tokens.Select(token => BuildPayload(token, message)).ToList();

        // Send in batches — each batch is independently error-handled
        using var client = httpClientFactory.CreateClient("ExpoPush");
        if (!string.IsNullOrWhiteSpace(config.AccessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AccessToken);
        }

        var staleTokens = new List<string>();
        for (int i = 0; i < payloads.Count; i += config.BatchSize)
        {
            var batch = payloads.Skip(i).Take(config.BatchSize).ToList();
            try
            {
                staleTokens.AddRange(await SendBatchAsync(client, batch, ct));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "First attempt failed for push batch {BatchIndex}, retrying once...",
                    i / config.BatchSize);
                try
                {
                    staleTokens.AddRange(await SendBatchAsync(client, batch, ct));
                }
                catch (Exception retryEx)
                {
                    logger.LogError(retryEx, "Failed to send push batch {BatchIndex} for user {UserId} ({TokenCount} tokens)",
                        i / config.BatchSize, message.UserId, batch.Count);
                }
            }
        }

        // Remove stale tokens using the already-open context
        if (staleTokens.Count > 0)
        {
            try
            {
                await context.PushTokens
                    .Where(pt => staleTokens.Contains(pt.Token))
                    .ExecuteDeleteAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove {Count} stale push token(s) for user {UserId}; will retry on next delivery.",
                    staleTokens.Count, message.UserId);
            }
        }
    }

    private static ExpoPushPayload BuildPayload(string token, PushNotificationMessage message)
    {
        Dictionary<string, object>? data = null;
        if (message.Route != null)
        {
            var routeData = new Dictionary<string, string> { ["screen"] = message.Route.Screen };
            if (message.Route.IssueId != null)
                routeData["issueId"] = message.Route.IssueId;

            data = new Dictionary<string, object> { ["route"] = routeData };
        }

        return new ExpoPushPayload
        {
            To = token,
            Title = message.Title,
            Body = message.Body,
            Sound = "default",
            Data = data
        };
    }

    /// <summary>
    /// Sends a batch to Expo and returns any stale tokens (DeviceNotRegistered) for removal.
    /// </summary>
    private async Task<List<string>> SendBatchAsync(HttpClient client, List<ExpoPushPayload> batch, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(batch, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(ExpoPushUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Expo push API returned {StatusCode}: {Body}", response.StatusCode, body);
            throw new HttpRequestException(
                $"Expo push API returned {response.StatusCode}: {body}");
        }

        // Parse response to collect stale tokens
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return CollectStaleTokens(responseBody, batch);
    }

    private List<string> CollectStaleTokens(string responseBody, List<ExpoPushPayload> batch)
    {
        var staleTokens = new List<string>();
        try
        {
            var result = JsonSerializer.Deserialize<ExpoPushResponse>(responseBody, JsonOptions);
            if (result?.Data == null) return staleTokens;

            for (int i = 0; i < result.Data.Count && i < batch.Count; i++)
            {
                var ticket = result.Data[i];
                if (ticket.Status == "ok") continue;

                if (ticket.Details?.Error == "DeviceNotRegistered")
                {
                    staleTokens.Add(batch[i].To);
                    logger.LogInformation("Push token no longer registered, removing (token ending ...{Suffix})",
                        batch[i].To.Length > 8 ? batch[i].To[^8..] : batch[i].To);
                }
                else
                {
                    logger.LogError("Expo push ticket error for token ending ...{Suffix}: {Error} (status: {Status})",
                        batch[i].To.Length > 8 ? batch[i].To[^8..] : batch[i].To,
                        ticket.Details?.Error ?? "unknown", ticket.Status);
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Expo push response. Raw body: {ResponseBody}", responseBody);
        }
        return staleTokens;
    }

    // DTOs for Expo Push API
    private sealed class ExpoPushPayload
    {
        public string To { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? Sound { get; set; }
        public Dictionary<string, object>? Data { get; set; }
    }

    private sealed class ExpoPushResponse
    {
        public List<ExpoPushTicket>? Data { get; set; }
    }

    private sealed class ExpoPushTicket
    {
        public string Status { get; set; } = string.Empty;
        public ExpoPushTicketDetails? Details { get; set; }
    }

    private sealed class ExpoPushTicketDetails
    {
        public string? Error { get; set; }
    }
}
