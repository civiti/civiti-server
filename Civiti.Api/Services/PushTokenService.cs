using Civiti.Api.Data;
using Civiti.Api.Models.Domain;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Api.Services;

public class PushTokenService(
    CivitiDbContext context,
    ILogger<PushTokenService> logger) : IPushTokenService
{
    public async Task RegisterTokenAsync(Guid userId, string token, string platform, CancellationToken ct = default)
    {
        if (!Enum.TryParse<PushTokenPlatform>(platform, ignoreCase: true, out var parsedPlatform))
            throw new ArgumentException($"Invalid platform: {platform}. Must be 'ios' or 'android'.");

        try
        {
            await UpsertTokenAsync(userId, token, parsedPlatform, ct);
        }
        catch (DbUpdateException ex)
        {
            // Race condition: another request inserted the same token concurrently.
            // Retry once — the token now exists, so the upsert will take the update path.
            // Note: UpsertTokenAsync clears the ChangeTracker at the start of its lambda.
            logger.LogDebug(ex, "Race condition detected registering token for user {UserId}, retrying once", userId);
            await UpsertTokenAsync(userId, token, parsedPlatform, ct);
        }
    }

    private const int MaxTokensPerUser = 10;

    private async Task UpsertTokenAsync(Guid userId, string token, PushTokenPlatform platform, CancellationToken ct)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async (cancellationToken) =>
        {
            context.ChangeTracker.Clear();
            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

            PushToken? existing = await context.PushTokens
                .FirstOrDefaultAsync(pt => pt.Token == token, cancellationToken);

            if (existing != null)
            {
                if (existing.UserId != userId)
                {
                    logger.LogInformation("Reassigning push token to user {NewUserId}", userId);
                    existing.UserId = userId;
                }

                existing.Platform = platform;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                context.PushTokens.Add(new PushToken
                {
                    UserId = userId,
                    Token = token,
                    Platform = platform
                });
            }

            await context.SaveChangesAsync(cancellationToken);

            // Enforce per-user token cap by removing the oldest excess tokens.
            // ExecuteDeleteAsync bypasses the change tracker, but this is safe on retry:
            // ChangeTracker.Clear() resets state and the rolled-back transaction means
            // excess tokens are re-found and re-deleted idempotently.
            var excessTokenIds = await context.PushTokens
                .Where(pt => pt.UserId == userId)
                .OrderByDescending(pt => pt.UpdatedAt)
                .Skip(MaxTokensPerUser)
                .Select(pt => pt.Id)
                .ToListAsync(cancellationToken);

            if (excessTokenIds.Count > 0)
            {
                await context.PushTokens
                    .Where(pt => excessTokenIds.Contains(pt.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }, ct);
    }

    public async Task DeregisterTokenAsync(Guid userId, string token, CancellationToken ct = default)
    {
        int deleted = await context.PushTokens
            .Where(pt => pt.Token == token && pt.UserId == userId)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            logger.LogInformation("Deregistered push token for user {UserId}", userId);
        }
    }
}
