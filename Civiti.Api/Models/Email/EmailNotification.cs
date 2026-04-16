namespace Civiti.Api.Models.Email;

/// <summary>
/// Message record passed through the Channel for async email delivery
/// </summary>
public record EmailNotification(
    string To,
    string Subject,
    string HtmlBody,
    EmailNotificationType Type);

/// <summary>
/// Types of email notifications sent by the system
/// </summary>
public enum EmailNotificationType
{
    IssueSubmitted,
    IssueApproved,
    IssueRejected,
    ChangesRequested,
    IssueResolved,
    IssueCancelled,
    NewCommentOnIssue,
    ReplyToComment,
    VoteMilestone,
    EmailSupportMilestone,
    LevelUp,
    BadgeEarned,
    AchievementCompleted,
    Welcome,
    AdminNewIssue
}
