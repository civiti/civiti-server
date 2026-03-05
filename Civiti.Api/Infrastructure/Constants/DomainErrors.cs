namespace Civiti.Api.Infrastructure.Constants;

/// <summary>
/// Centralised domain error messages used in services and endpoints.
/// Matching on these constants (instead of string literals) prevents
/// silent breakage from typos in catch-when guards and switch expressions.
/// </summary>
public static class DomainErrors
{
    public const string AccountDeleted = "This account has been deleted.";
    public const string UserNotFound = "User not found";
    public const string UserProfileNotFound = "User profile not found";
    public const string IssueNotFound = "Issue not found";
}
