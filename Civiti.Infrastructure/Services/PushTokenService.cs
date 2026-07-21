using Civiti.Infrastructure.Data;
using Civiti.Domain.Entities;
using Civiti.Application.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Civiti.Infrastructure.Services;

public class PushTokenService(
    CivitiDbContext context,
    ILogger<PushTokenService> logger) : IPushTokenService
{
    public async Task RegisterTokenAsync(Guid userId, string token, string platform, string? deviceId = null, CancellationToken ct = default)
    {
        if (!Enum.TryParse<PushTokenPlatform>(platform, ignoreCase: true, out var parsedPlatform))
            throw new ArgumentException($"Invalid platform: {platform}. Must be 'ios' or 'android'.");

        // Normalize empty/whitespace device ids to null so blank values don't collapse
        // every deviceless row for the user into one.
        var normalizedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;

        try
        {
            await UpsertTokenAsync(userId, token, parsedPlatform, normalizedDeviceId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Unique-constraint violation: another request inserted the same token concurrently.
            // Retry once — the token now exists, so the upsert will take the update path.
            logger.LogDebug(ex, "Race condition detected registering token for user {UserId}, retrying once", userId);
            await UpsertTokenAsync(userId, token, parsedPlatform, normalizedDeviceId, ct);
        }
    }

    private const int MaxTokensPerUser = 10;

    private async Task UpsertTokenAsync(Guid userId, string token, PushTokenPlatform platform, string? deviceId, CancellationToken ct)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async (cancellationToken) =>
        {
            context.ChangeTracker.Clear();
            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

            // Serialize concurrent registrations for the same (user, device). Without this,
            // two overlapping transactions rotating the device's token each run the collapse
            // below before the other's insert commits, so neither sees the other's row and
            // both survive — leaving the device with duplicate tokens. The advisory lock is
            // transaction-scoped (released on commit/rollback), so it composes with the retry
            // execution strategy and forces the later registration to observe the earlier
            // committed row. Postgres-only: the SQLite test provider serializes writes on its
            // single connection and has no such function.
            if (deviceId != null && context.Database.IsNpgsql())
            {
                var deviceLockKey = $"push-token:{userId:N}:{deviceId}";
                await context.Database.ExecuteSqlAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({deviceLockKey}, 0))",
                    cancellationToken);
            }

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
                if (deviceId != null)
                    existing.DeviceId = deviceId;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                context.PushTokens.Add(new PushToken
                {
                    UserId = userId,
                    Token = token,
                    Platform = platform,
                    DeviceId = deviceId
                });
            }

            await context.SaveChangesAsync(cancellationToken);

            // Collapse this device's stale tokens: a device re-registering with a rotated
            // token should replace its old row, not accumulate one. Only applies when the
            // client supplies a device id (older clients send none and keep prior behavior).
            if (deviceId != null)
            {
                var supersededTokenIds = await context.PushTokens
                    .Where(pt => pt.UserId == userId && pt.DeviceId == deviceId && pt.Token != token)
                    .Select(pt => pt.Id)
                    .ToListAsync(cancellationToken);

                if (supersededTokenIds.Count > 0)
                {
                    await context.PushTokens
                        .Where(pt => supersededTokenIds.Contains(pt.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                }
            }

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
