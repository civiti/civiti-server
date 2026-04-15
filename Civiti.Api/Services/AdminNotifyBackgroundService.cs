using System.Threading.Channels;
using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Api.Infrastructure.Email;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Email;
using Civiti.Api.Models.Notifications;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

// Intentionally not "using static EmailDataKeys" here — EmailDataKeys.IssueCategory
// collides with the IssueCategory enum imported above, so we reference keys explicitly.

namespace Civiti.Api.Services;

/// <summary>
/// Drains the admin-notify channel and, for each request:
/// <list type="number">
///   <item>Loads the issue (with author) from the DB;</item>
///   <item>Asks <see cref="ISupabaseAdminClient"/> for the admin list (cached);</item>
///   <item>Inserts a per-(issue, admin) audit row — the composite PK gives us
///         per-recipient idempotency if the dispatch is ever retried;</item>
///   <item>Renders the template and enqueues onto the existing email channel.</item>
/// </list>
/// Errors are logged, never thrown, so an individual bad request can't take the service down.
/// </summary>
public sealed class AdminNotifyBackgroundService(
    ChannelReader<AdminNotifyRequest> channelReader,
    IServiceScopeFactory scopeFactory,
    ILogger<AdminNotifyBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Admin notifier background service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (AdminNotifyRequest request in channelReader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await ProcessRequestAsync(request, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException oce || oce.CancellationToken != stoppingToken)
                    {
                        logger.LogError(ex, "Failed to process admin-notify request for issue {IssueId}", request.IssueId);
                    }
                }

                // Channel completed normally.
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Admin notifier crashed — restarting in 5 seconds");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("Admin notifier background service stopping");
    }

    private async Task ProcessRequestAsync(AdminNotifyRequest request, CancellationToken ct)
    {
        // Only one event type today; guard in case future types slip in without handling.
        if (request.Type != AdminNotifyEventType.NewIssueSubmitted)
        {
            logger.LogWarning("Unhandled admin notify event type {Type} for issue {IssueId} — dropping.",
                request.Type, request.IssueId);
            return;
        }

        using IServiceScope scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CivitiDbContext>();
        var adminClient = scope.ServiceProvider.GetRequiredService<ISupabaseAdminClient>();
        var templateService = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();
        var emailWriter = scope.ServiceProvider.GetRequiredService<ChannelWriter<EmailNotification>>();
        var resendConfig = scope.ServiceProvider.GetRequiredService<ResendConfiguration>();

        // Load issue + author. IgnoreQueryFilters because we don't want a soft-delete filter
        // to silently drop the notification.
        Issue? issue = await dbContext.Issues
            .IgnoreQueryFilters()
            .Include(i => i.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.IssueId, ct);

        if (issue == null)
        {
            logger.LogWarning("Admin-notify skipped: issue {IssueId} not found (may have been hard-deleted).", request.IssueId);
            return;
        }

        IReadOnlyList<SupabaseAdminUser> admins;
        try
        {
            admins = await adminClient.ListAdminsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException oce || oce.CancellationToken != ct)
        {
            logger.LogError(ex, "Failed to list admins for issue {IssueId} — aborting notification.", issue.Id);
            return;
        }

        if (admins.Count == 0)
        {
            logger.LogWarning("No admins found — skipping admin notification for issue {IssueId}.", issue.Id);
            return;
        }

        var templateData = BuildTemplateData(issue, resendConfig.FrontendBaseUrl);
        (string subject, string htmlBody) = templateService.Render(EmailNotificationType.AdminNewIssue, templateData);

        foreach (SupabaseAdminUser admin in admins)
        {
            var normalizedEmail = admin.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedEmail))
            {
                continue;
            }

            // Per-recipient idempotency: skip if a prior dispatch already notified this admin.
            var alreadyNotified = await dbContext.AdminIssueNotifications
                .AsNoTracking()
                .AnyAsync(n => n.IssueId == issue.Id && n.AdminEmail == normalizedEmail, ct);

            if (alreadyNotified)
            {
                logger.LogDebug("Admin {AdminEmail} already notified for issue {IssueId}; skipping.",
                    normalizedEmail, issue.Id);
                continue;
            }

            // Step 1: try to enqueue the email *first*. If the channel is full (DropWrite),
            // DO NOT persist the audit row — otherwise a future retry would see the audit
            // and skip this admin permanently, silently losing the notification.
            EmailNotification message = new(normalizedEmail, subject, htmlBody, EmailNotificationType.AdminNewIssue);
            if (!emailWriter.TryWrite(message))
            {
                logger.LogError(
                    "Email channel full — dropped admin-new-issue email: issue={IssueId} admin={AdminEmail}. "
                    + "Increase Resend:ChannelCapacity if this persists. Audit row NOT persisted so a retry can re-attempt.",
                    issue.Id, normalizedEmail);
                continue;
            }

            // Step 2: enqueue succeeded — record the audit row so subsequent dispatches skip this admin.
            // The composite PK (IssueId, AdminEmail) is the final backstop against concurrent workers;
            // if another worker raced us we've already emitted one extra email (acceptable corner case,
            // single-consumer channel in practice makes this vanishingly rare).
            try
            {
                dbContext.AdminIssueNotifications.Add(new AdminIssueNotification
                {
                    IssueId = issue.Id,
                    AdminEmail = normalizedEmail,
                    EnqueuedAt = DateTime.UtcNow
                });

                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Enqueued admin-new-issue email: issue={IssueId} admin={AdminEmail}",
                    issue.Id, normalizedEmail);
            }
            catch (DbUpdateException ex)
            {
                // Unique-violation (concurrent worker) or another insert failure. The email has already
                // been enqueued; we just can't record it here. Next run's AnyAsync check will see the
                // winner's row and skip.
                logger.LogWarning(ex,
                    "Audit row insert failed after email enqueue for issue {IssueId}, admin {AdminEmail}.",
                    issue.Id, normalizedEmail);
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private static Dictionary<string, string> BuildTemplateData(Issue issue, string frontendBaseUrl)
    {
        // submitter name: prefer UserProfile.DisplayName; fall back to email prefix.
        var submitterName = issue.User?.DisplayName;
        if (string.IsNullOrWhiteSpace(submitterName))
        {
            submitterName = issue.User?.Email?.Split('@').FirstOrDefault();
        }
        submitterName ??= "Anonim";

        // address line: include district if present ("Sector 2 · Strada X")
        var addressLine = !string.IsNullOrWhiteSpace(issue.District)
            ? $"{issue.District} · {issue.Address}"
            : issue.Address;

        return new Dictionary<string, string>
        {
            [EmailDataKeys.IssueTitle] = issue.Title,
            [EmailDataKeys.UserName] = submitterName,
            [EmailDataKeys.IssueCategory] = issue.Category.ToString(),
            [EmailDataKeys.IssueAddress] = addressLine,
            [EmailDataKeys.IssueUrgency] = issue.Urgency.ToString(),
            [EmailDataKeys.CtaUrl] = $"{frontendBaseUrl.TrimEnd('/')}/admin/issues/{issue.Id}",
            [EmailDataKeys.CtaText] = "Deschide în panoul admin"
        };
    }

}
