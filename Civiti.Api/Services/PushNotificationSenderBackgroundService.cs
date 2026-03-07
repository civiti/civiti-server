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
        bool enabled = await context.UserProfiles
            .Where(up => up.Id == message.UserId)
            .Select(up => up.PushNotificationsEnabled)
            .FirstOrDefaultAsync(ct);

        if (!enabled) return;

        // Resolve user's push tokens
        List<string> tokens = await context.PushTokens
            .Where(pt => pt.UserId == message.UserId)
            .Select(pt => pt.Token)
            .ToListAsync(ct);

        if (tokens.Count == 0) return;

        // Build Expo payloads
        var payloads = tokens.Select(token => BuildPayload(token, message)).ToList();

        // Send in batches — each batch is independently error-handled
        for (int i = 0; i < payloads.Count; i += config.BatchSize)
        {
            var batch = payloads.Skip(i).Take(config.BatchSize).ToList();
            try
            {
                await SendBatchAsync(batch, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send push batch {BatchIndex} for user {UserId} ({TokenCount} tokens)",
                    i / config.BatchSize, message.UserId, batch.Count);
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

    private async Task SendBatchAsync(List<ExpoPushPayload> batch, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("ExpoPush");

        if (!string.IsNullOrWhiteSpace(config.AccessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AccessToken);
        }

        var json = JsonSerializer.Serialize(batch, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(ExpoPushUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Expo push API returned {StatusCode}: {Body}", response.StatusCode, body);
            return;
        }

        // Parse response to handle per-ticket errors
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        await HandleExpoPushResponseAsync(responseBody, batch, ct);
    }

    private async Task HandleExpoPushResponseAsync(string responseBody, List<ExpoPushPayload> batch, CancellationToken ct)
    {
        try
        {
            var result = JsonSerializer.Deserialize<ExpoPushResponse>(responseBody, JsonOptions);
            if (result?.Data == null) return;

            var tokensToRemove = new List<string>();

            for (int i = 0; i < result.Data.Count && i < batch.Count; i++)
            {
                var ticket = result.Data[i];
                if (ticket.Status == "ok") continue;

                if (ticket.Details?.Error == "DeviceNotRegistered")
                {
                    tokensToRemove.Add(batch[i].To);
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

            if (tokensToRemove.Count > 0)
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<CivitiDbContext>();
                await context.PushTokens
                    .Where(pt => tokensToRemove.Contains(pt.Token))
                    .ExecuteDeleteAsync(ct);
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Expo push response. Raw body: {ResponseBody}", responseBody);
        }
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
