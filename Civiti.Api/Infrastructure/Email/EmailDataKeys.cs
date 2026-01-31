namespace Civiti.Api.Infrastructure.Email;

/// <summary>
/// Shared template data dictionary keys used by NotificationService and EmailTemplates.
/// Single source of truth to keep producer and consumer in sync.
/// </summary>
public static class EmailDataKeys
{
    public const string UserName = "UserName";
    public const string IssueTitle = "IssueTitle";
    public const string CtaUrl = "CtaUrl";
    public const string CtaText = "CtaText";
    public const string Reason = "Reason";
    public const string Notes = "Notes";
    public const string CommenterName = "CommenterName";
    public const string CommentExcerpt = "CommentExcerpt";
    public const string ReplierName = "ReplierName";
    public const string ReplyExcerpt = "ReplyExcerpt";
    public const string VoteCount = "VoteCount";
    public const string EmailCount = "EmailCount";
    public const string Level = "Level";
    public const string BadgeName = "BadgeName";
    public const string AchievementName = "AchievementName";
}
