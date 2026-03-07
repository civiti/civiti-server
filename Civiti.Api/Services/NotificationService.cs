using System.Threading.Channels;
using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Email;
using Civiti.Api.Models.Push;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using static Civiti.Api.Infrastructure.Email.EmailDataKeys;

namespace Civiti.Api.Services;

/// <summary>
/// High-level notification facade. Checks user preferences, renders templates,
/// debounces where needed, and enqueues both emails and push notifications
/// for async background delivery. All methods are safe to call in fire-and-forget fashion.
/// </summary>
public class NotificationService(
    ILogger<NotificationService> logger,
    IEmailTemplateService templateService,
    ChannelWriter<EmailNotification> emailChannelWriter,
    ChannelWriter<PushNotificationMessage> pushChannelWriter,
    ResendConfiguration config,
    IMemoryCache memoryCache,
    CivitiDbContext context) : INotificationService
{
    private static readonly int[] VoteMilestones = [5, 10, 25, 50, 100, 250, 500];

    // --- Issue Lifecycle (IssueUpdatesEnabled) ---

    public Task NotifyIssueSubmittedAsync(Issue issue, UserProfile author)
    {
        if (!author.IssueUpdatesEnabled) return Task.CompletedTask;

        EnqueuePush(author, "Problemă trimisă", $"\"{issue.Title}\" a fost trimisă spre aprobare.",
            new PushRoute("issue", issue.Id.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.IssueSubmitted, author.Email, new Dictionary<string, string>
        {
            [UserName] = author.DisplayName,
            [IssueTitle] = issue.Title,
            [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issue.Id}",
            [CtaText] = "Vezi problema"
        });
    }

    public Task NotifyIssueApprovedAsync(Issue issue, UserProfile author)
    {
        if (!author.IssueUpdatesEnabled) return Task.CompletedTask;

        EnqueuePush(author, "Problemă aprobată", $"\"{issue.Title}\" a fost aprobată și este acum publică.",
            new PushRoute("issue", issue.Id.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.IssueApproved, author.Email, new Dictionary<string, string>
        {
            [UserName] = author.DisplayName,
            [IssueTitle] = issue.Title,
            [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issue.Id}",
            [CtaText] = "Vezi problema"
        });
    }

    public Task NotifyIssueRejectedAsync(Issue issue, UserProfile author, string reason)
    {
        if (!author.IssueUpdatesEnabled) return Task.CompletedTask;

        EnqueuePush(author, "Problemă respinsă", $"\"{issue.Title}\" a fost respinsă: {Truncate(reason, 100)}",
            new PushRoute("issue", issue.Id.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.IssueRejected, author.Email, new Dictionary<string, string>
        {
            [UserName] = author.DisplayName,
            [IssueTitle] = issue.Title,
            [Reason] = reason
        });
    }

    public Task NotifyChangesRequestedAsync(Issue issue, UserProfile author, string notes)
    {
        if (!author.IssueUpdatesEnabled) return Task.CompletedTask;

        EnqueuePush(author, "Modificări necesare", $"\"{issue.Title}\" necesită modificări.",
            new PushRoute("issue", issue.Id.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.ChangesRequested, author.Email, new Dictionary<string, string>
        {
            [UserName] = author.DisplayName,
            [IssueTitle] = issue.Title,
            [Notes] = notes,
            [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issue.Id}/edit",
            [CtaText] = "Editează problema"
        });
    }

    public async Task NotifyIssueResolvedAsync(Issue issue, UserProfile author, CancellationToken cancellationToken = default)
    {
        // Notify author
        if (author.IssueUpdatesEnabled)
        {
            EnqueuePush(author, "Problemă rezolvată", $"\"{issue.Title}\" a fost marcată ca rezolvată!",
                new PushRoute("issue", issue.Id.ToString()));

            await EnqueueEmailAsync(EmailNotificationType.IssueResolved, author.Email, new Dictionary<string, string>
            {
                [UserName] = author.DisplayName,
                [IssueTitle] = issue.Title,
                [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issue.Id}",
                [CtaText] = "Vezi problema"
            });
        }

        // Notify voters and commenters (distinct, excluding author)
        await NotifyIssueFollowersAsync(issue.Id, issue.Title, author.Id,
            EmailNotificationType.IssueResolved, cancellationToken);
    }

    public async Task NotifyIssueCancelledAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        Issue? issue = await context.Issues
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue == null) return;

        await NotifyIssueFollowersAsync(issueId, issue.Title, issue.UserId,
            EmailNotificationType.IssueCancelled, cancellationToken);
    }

    // --- Community Engagement ---

    public Task NotifyNewCommentOnIssueAsync(Issue issue, UserProfile issueAuthor, UserProfile commenter, string commentExcerpt)
    {
        if (!issueAuthor.IssueUpdatesEnabled || issueAuthor.Id == commenter.Id) return Task.CompletedTask;

        // Debounce: max 1 notification per 5 min per issue author per issue
        var debounceKey = $"notify:comment:{issue.Id}:{issueAuthor.Id}";
        if (memoryCache.TryGetValue(debounceKey, out _)) return Task.CompletedTask;
        memoryCache.Set(debounceKey, true, TimeSpan.FromMinutes(config.DebounceMinutes));

        EnqueuePush(issueAuthor, "Comentariu nou",
            $"{commenter.DisplayName} a comentat la \"{Truncate(issue.Title, 40)}\"",
            new PushRoute("issue", issue.Id.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.NewCommentOnIssue, issueAuthor.Email, new Dictionary<string, string>
        {
            [UserName] = issueAuthor.DisplayName,
            [IssueTitle] = issue.Title,
            [CommenterName] = commenter.DisplayName,
            [CommentExcerpt] = Truncate(commentExcerpt, 200),
            [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issue.Id}",
            [CtaText] = "Vezi comentariul"
        });
    }

    public Task NotifyCommentReplyAsync(UserProfile parentCommentUser, UserProfile replier, Guid issueId, string replyExcerpt)
    {
        if (parentCommentUser.Id == replier.Id) return Task.CompletedTask;
        if (!parentCommentUser.IssueUpdatesEnabled) return Task.CompletedTask;

        // Debounce: max 1 reply notification per 5 min per parent comment user
        var debounceKey = $"notify:reply:{parentCommentUser.Id}:{issueId}";
        if (memoryCache.TryGetValue(debounceKey, out _)) return Task.CompletedTask;
        memoryCache.Set(debounceKey, true, TimeSpan.FromMinutes(config.DebounceMinutes));

        EnqueuePush(parentCommentUser, "Răspuns la comentariu",
            $"{replier.DisplayName} ți-a răspuns la un comentariu.",
            new PushRoute("issue", issueId.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.ReplyToComment, parentCommentUser.Email, new Dictionary<string, string>
        {
            [UserName] = parentCommentUser.DisplayName,
            [ReplierName] = replier.DisplayName,
            [ReplyExcerpt] = Truncate(replyExcerpt, 200),
            [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issueId}",
            [CtaText] = "Vezi răspunsul"
        });
    }

    public Task NotifyVoteMilestoneAsync(Issue issue, UserProfile author, int voteCount)
    {
        if (!author.IssueUpdatesEnabled || !IsMilestone(voteCount)) return Task.CompletedTask;

        EnqueuePush(author, "Prag de voturi atins",
            $"\"{Truncate(issue.Title, 40)}\" a atins {voteCount} voturi!",
            new PushRoute("issue", issue.Id.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.VoteMilestone, author.Email, new Dictionary<string, string>
        {
            [UserName] = author.DisplayName,
            [IssueTitle] = issue.Title,
            [VoteCount] = voteCount.ToString(),
            [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issue.Id}",
            [CtaText] = "Vezi problema"
        });
    }

    public Task NotifyEmailSupportMilestoneAsync(Issue issue, UserProfile author, int emailCount)
    {
        if (!author.IssueUpdatesEnabled || !IsMilestone(emailCount)) return Task.CompletedTask;

        EnqueuePush(author, "Susținere prin email",
            $"\"{Truncate(issue.Title, 40)}\" a primit {emailCount} emailuri de susținere!",
            new PushRoute("issue", issue.Id.ToString()));

        return EnqueueEmailAsync(EmailNotificationType.EmailSupportMilestone, author.Email, new Dictionary<string, string>
        {
            [UserName] = author.DisplayName,
            [IssueTitle] = issue.Title,
            [EmailCount] = emailCount.ToString(),
            [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issue.Id}",
            [CtaText] = "Vezi problema"
        });
    }

    // --- Gamification (AchievementsEnabled) ---

    public Task NotifyLevelUpAsync(UserProfile user, int newLevel)
    {
        if (!user.AchievementsEnabled) return Task.CompletedTask;

        EnqueuePush(user, "Nivel nou!", $"Felicitări! Ai avansat la nivelul {newLevel}.",
            new PushRoute("achievements"));

        return EnqueueEmailAsync(EmailNotificationType.LevelUp, user.Email, new Dictionary<string, string>
        {
            [UserName] = user.DisplayName,
            [Level] = newLevel.ToString(),
            [CtaUrl] = $"{config.FrontendBaseUrl}/dashboard",
            [CtaText] = "Vezi profilul"
        });
    }

    public Task NotifyBadgeEarnedAsync(UserProfile user, string badgeName)
    {
        if (!user.AchievementsEnabled) return Task.CompletedTask;

        EnqueuePush(user, "Insignă nouă!", $"Ai primit insigna \"{badgeName}\".",
            new PushRoute("badges"));

        return EnqueueEmailAsync(EmailNotificationType.BadgeEarned, user.Email, new Dictionary<string, string>
        {
            [UserName] = user.DisplayName,
            [BadgeName] = badgeName,
            [CtaUrl] = $"{config.FrontendBaseUrl}/dashboard",
            [CtaText] = "Vezi insignele"
        });
    }

    public Task NotifyAchievementCompletedAsync(UserProfile user, string achievementName)
    {
        if (!user.AchievementsEnabled) return Task.CompletedTask;

        EnqueuePush(user, "Realizare completată!", $"Ai completat realizarea \"{achievementName}\".",
            new PushRoute("achievements"));

        return EnqueueEmailAsync(EmailNotificationType.AchievementCompleted, user.Email, new Dictionary<string, string>
        {
            [UserName] = user.DisplayName,
            [AchievementName] = achievementName,
            [CtaUrl] = $"{config.FrontendBaseUrl}/dashboard",
            [CtaText] = "Vezi realizările"
        });
    }

    // --- Account (always sent) ---

    public Task NotifyWelcomeAsync(UserProfile user)
    {
        // Welcome push is always sent (no preference check)
        EnqueuePush(user, "Bine ai venit!", "Contul tău Civiti a fost creat cu succes.", forceSend: true);

        return EnqueueEmailAsync(EmailNotificationType.Welcome, user.Email, new Dictionary<string, string>
        {
            [UserName] = user.DisplayName,
            [CtaUrl] = $"{config.FrontendBaseUrl}/dashboard",
            [CtaText] = "Începe acum"
        });
    }

    // --- Helpers ---

    private Task EnqueueEmailAsync(EmailNotificationType type, string to, Dictionary<string, string> data)
    {
        try
        {
            var (subject, htmlBody) = templateService.Render(type, data);
            EmailNotification notification = new(to, subject, htmlBody, type);

            if (!emailChannelWriter.TryWrite(notification))
            {
                logger.LogError("Email channel full — dropped {Type} email to {To}. Increase Resend:ChannelCapacity if this persists.", type, to);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue {Type} email to {To}", type, to);
        }

        return Task.CompletedTask;
    }

    private void EnqueuePush(UserProfile user, string title, string body, PushRoute? route = null, bool forceSend = false)
    {
        if (!forceSend && !user.PushNotificationsEnabled) return;

        try
        {
            var message = new PushNotificationMessage(user.Id, title, body, route, forceSend);
            if (!pushChannelWriter.TryWrite(message))
            {
                logger.LogError("Push channel full — dropped push for user {UserId}. Increase ExpoPush:ChannelCapacity if this persists.", user.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue push notification for user {UserId}", user.Id);
        }
    }

    private static bool IsMilestone(int count) => Array.Exists(VoteMilestones, m => m == count);

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";

    private async Task NotifyIssueFollowersAsync(
        Guid issueId, string issueTitle, Guid excludeUserId,
        EmailNotificationType type, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get distinct user IDs who voted or commented (excluding the author)
            List<Guid> voterIds = await context.IssueVotes
                .Where(v => v.IssueId == issueId && v.UserId != excludeUserId)
                .Select(v => v.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            List<Guid> commenterIds = await context.Comments
                .Where(c => c.IssueId == issueId && c.UserId != excludeUserId && !c.IsDeleted)
                .Select(c => c.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            List<Guid> followerIds = voterIds.Union(commenterIds).Distinct().ToList();

            if (followerIds.Count == 0) return;

            // Get their profiles with preference check
            List<UserProfile> followers = await context.UserProfiles
                .AsNoTracking()
                .Where(u => followerIds.Contains(u.Id) && u.IssueUpdatesEnabled)
                .ToListAsync(cancellationToken);

            var (pushTitle, statusText) = type switch
            {
                EmailNotificationType.IssueResolved => ("Problemă rezolvată", "rezolvată"),
                EmailNotificationType.IssueCancelled => ("Problemă anulată", "anulată"),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported notification type for issue followers")
            };
            var pushBody = $"\"{Truncate(issueTitle, 40)}\" a fost {statusText}.";

            foreach (UserProfile follower in followers)
            {
                EnqueuePush(follower, pushTitle, pushBody, new PushRoute("issue", issueId.ToString()));

                await EnqueueEmailAsync(type, follower.Email, new Dictionary<string, string>
                {
                    [UserName] = follower.DisplayName,
                    [IssueTitle] = issueTitle,
                    [CtaUrl] = $"{config.FrontendBaseUrl}/issue/{issueId}",
                    [CtaText] = "Vezi problema"
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to notify followers for issue {IssueId}", issueId);
        }
    }
}
