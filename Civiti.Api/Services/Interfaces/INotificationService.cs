using Civiti.Api.Models.Domain;

namespace Civiti.Api.Services.Interfaces;

/// <summary>
/// High-level notification facade. Checks user preferences, renders templates,
/// and enqueues emails for async delivery. Calls are fire-and-forget safe.
/// </summary>
public interface INotificationService
{
    // Issue lifecycle
    Task NotifyIssueSubmittedAsync(Issue issue, UserProfile author);
    Task NotifyIssueApprovedAsync(Issue issue, UserProfile author);
    Task NotifyIssueRejectedAsync(Issue issue, UserProfile author, string reason);
    Task NotifyChangesRequestedAsync(Issue issue, UserProfile author, string notes);
    Task NotifyIssueResolvedAsync(Issue issue, UserProfile author, CancellationToken cancellationToken = default);
    Task NotifyIssueCancelledAsync(Guid issueId, CancellationToken cancellationToken = default);

    // Community engagement
    Task NotifyNewCommentOnIssueAsync(Issue issue, UserProfile issueAuthor, UserProfile commenter, string commentExcerpt);
    Task NotifyCommentReplyAsync(UserProfile parentCommentUser, UserProfile replier, Guid issueId, string replyExcerpt);
    Task NotifyVoteMilestoneAsync(Issue issue, UserProfile author, int voteCount);
    Task NotifyEmailSupportMilestoneAsync(Issue issue, UserProfile author, int emailCount);

    // Gamification
    Task NotifyLevelUpAsync(UserProfile user, int newLevel);
    Task NotifyBadgeEarnedAsync(UserProfile user, string badgeName);
    Task NotifyAchievementCompletedAsync(UserProfile user, string achievementName);

    // Account
    Task NotifyWelcomeAsync(UserProfile user);
}
