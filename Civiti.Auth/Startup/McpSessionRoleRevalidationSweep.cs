using Civiti.Application.Services;
using Civiti.Infrastructure.Configuration;
using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Civiti.Auth.Startup;

/// <summary>
/// Background sweep — auth-design.md §4 / §5. Every five minutes, walks the active
/// admin-scoped <see cref="Civiti.Domain.Entities.McpSession"/> rows and re-validates each
/// against the upstream Supabase user. A user demoted from admin between refreshes (or
/// disabled outright) is otherwise stuck holding admin-scoped access tokens until the next
/// refresh window, which can be 15 minutes — or longer if the client refreshes lazily. The
/// sweep closes that gap.
///
/// For each stale session: stamp <see cref="Civiti.Domain.Entities.McpSession.RevokedAt"/>
/// + reason and call <see cref="IOpenIddictTokenManager.TryRevokeAsync"/> on the linked
/// refresh token id so subsequent /token refresh attempts return invalid_grant immediately.
/// Only sessions whose <see cref="Civiti.Domain.Entities.McpSession.OpenIddictTokenId"/> is
/// populated participate (rows minted before v1b.3 have null linkage and won't be revoked
/// here — they'll be picked up on their next refresh by TokenEndpoint's per-refresh
/// re-validation, which v1b.2 wired).
/// </summary>
internal sealed class McpSessionRoleRevalidationSweep(
    IServiceScopeFactory scopeFactory,
    SupabaseConfiguration supabaseConfig,
    ILogger<McpSessionRoleRevalidationSweep> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly string[] AdminScopes = ["civiti.admin.read", "civiti.admin.write"];
    // First missing-key tick logs at Warning; subsequent ticks drop to Debug. Program.cs already
    // emits a startup warning, and the env var can't change at runtime (requires redeploy), so an
    // ongoing 5-minute warning would be pure noise in log aggregators after the first one.
    private bool _missingKeyWarningLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the first run so service start doesn't co-fire with the seeder + JWKS loaders.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Swallow + log; one bad pass shouldn't kill the sweep host.
                logger.LogError(ex, "McpSession role-revalidation sweep threw — continuing");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // Without the Supabase service role key we can't tell "user truly disabled" apart from
        // "we just can't reach the admin endpoint" — and ISupabaseAdminClient.GetUserAsync
        // collapses both into a null return. Without this guard a missing/cleared
        // SUPABASE_SERVICE_KEY at redeploy time would cause every admin-scoped active session
        // to be marked user_disabled within five minutes and the RevokedAt stamp would persist
        // even after the key is restored. Bail the whole pass instead — the worst case is a
        // demoted admin keeps their session until the next refresh-window re-validation
        // (TokenEndpoint already gates token issuance on the same service-key check), which
        // is strictly safer than a one-way mass revocation.
        if (!supabaseConfig.HasServiceRoleKey)
        {
            const string Message = "McpSession sweep skipping pass — SUPABASE_SERVICE_KEY not configured; cannot revalidate admin sessions safely";
            if (!_missingKeyWarningLogged)
            {
                logger.LogWarning(Message);
                _missingKeyWarningLogged = true;
            }
            else
            {
                logger.LogDebug(Message);
            }
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CivitiDbContext>();
        var supabase = scope.ServiceProvider.GetRequiredService<ISupabaseAdminClient>();
        var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        // Pull the admin-scoped active sessions. Npgsql translates List<string>.Any(...) over a
        // text[] column to ANY/array-overlap; the alternative (load all then filter) would scan
        // the whole table and doesn't scale beyond a few thousand sessions.
        var candidates = await dbContext.McpSessions
            .Where(s => s.RevokedAt == null
                        && s.OpenIddictTokenId != null
                        && AdminScopes.Any(adminScope => s.ScopesGranted.Contains(adminScope)))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return;

        logger.LogDebug("McpSession sweep: {Count} admin-scoped active session(s) to re-validate", candidates.Count);

        foreach (var session in candidates)
        {
            if (cancellationToken.IsCancellationRequested) return;

            string? revokeReason;
            try
            {
                // GetUserAsync now returns null only on legitimate "no record" cases (404 — user
                // truly deleted) and throws SupabaseTransientException on 5xx / 429 / network
                // errors. The catch below handles the transient case by skipping this session
                // (don't stamp RevokedAt during a Supabase outage); a null here means the user
                // is gone, which is exactly when we want to revoke.
                var snapshot = await supabase.GetUserAsync(session.SupabaseUserId, cancellationToken);
                if (snapshot is null)
                {
                    revokeReason = "user_disabled";
                }
                else if (snapshot.BannedUntilUtc is { } bannedUntil && bannedUntil > DateTime.UtcNow)
                {
                    revokeReason = "user_banned";
                }
                else if (!string.Equals(snapshot.Role, "admin", StringComparison.Ordinal))
                {
                    revokeReason = "role_lost";
                }
                else
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host shutdown — bubble up to ExecuteAsync's outer cancellation-aware catch
                // instead of logging a misleading "skipping session due to upstream lookup
                // failure" warning that suggests Supabase had a problem.
                throw;
            }
            catch (Exception ex)
            {
                // A transient GetUserAsync failure shouldn't poison the rest of the sweep —
                // skip this session and move on. Catching here (rather than at the loop level
                // outside) keeps already-committed revokes from being rolled back if a later
                // session raises.
                logger.LogWarning(ex,
                    "Sweep skipping session {SessionId} (sub {Sub}) due to upstream lookup failure",
                    session.Id, session.SupabaseUserId);
                continue;
            }

            session.RevokedAt = DateTime.UtcNow;
            session.RevokedReason = revokeReason;

            // Best-effort revoke of the refresh token. If the underlying entity is already gone
            // (manual cleanup, expired) TryRevokeAsync just returns false; we still keep the
            // McpSession row marked revoked.
            try
            {
                var token = await tokenManager.FindByIdAsync(session.OpenIddictTokenId!, cancellationToken);
                if (token is not null)
                {
                    await tokenManager.TryRevokeAsync(token, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host shutdown mid-revoke — let it bubble up. McpSession.RevokedAt is already
                // stamped in memory; SaveChangesAsync below would have committed it, but with
                // the host coming down we accept the loss rather than fight the shutdown clock.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Sweep failed to revoke OpenIddict token {TokenId} for session {SessionId}; McpSession still marked revoked",
                    session.OpenIddictTokenId, session.Id);
            }

            // Commit per-session so a later candidate's lookup failure can't strand
            // already-revoked OpenIddict tokens whose McpSession.RevokedAt update would
            // otherwise be lost when the outer try/catch swallows the exception. The cost is
            // one extra round-trip per stale session, which is negligible at the volumes we
            // expect (admin sessions are a small fraction of total sessions).
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "McpSession {SessionId} revoked by sweep (sub {Sub}, reason {Reason})",
                session.Id, session.SupabaseUserId, revokeReason);
        }
    }
}
