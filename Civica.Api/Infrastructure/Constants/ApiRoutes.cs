namespace Civica.Api.Infrastructure.Constants;

public static class ApiRoutes
{
    private const string ApiBase = "/api";
    
    public static class Auth
    {
        public const string Base = $"{ApiBase}/auth";
        public const string Profile = "/profile";
    }
    
    public static class Issues
    {
        public const string Base = $"{ApiBase}/issues";
        public const string ById = "/{id:guid}";
        public const string EmailSent = "/{id:guid}/email-sent";
    }
    
    public static class User
    {
        public const string Base = $"{ApiBase}/user";
        public const string Gamification = "/gamification";
        public const string Issues = "/issues";
        public const string Points = "/points";
    }
    
    public static class Admin
    {
        public const string Base = $"{ApiBase}/admin";
        public const string PendingIssues = "/pending-issues";
        public const string IssueById = "/issues/{id:guid}";
        public const string Approve = "/issues/{id:guid}/approve";
        public const string Reject = "/issues/{id:guid}/reject";
        public const string RequestChanges = "/issues/{id:guid}/request-changes";
        public const string Statistics = "/statistics";
        public const string BulkApprove = "/bulk-approve";
    }
    
    public static class Gamification
    {
        public const string Base = $"{ApiBase}/gamification";
        public const string Badges = "/badges";
        public const string Achievements = "/achievements";
        public const string Leaderboard = "/leaderboard";
    }
    
    public static class Utility
    {
        public const string Base = ApiBase;
        public const string Health = "/health";
        public const string Categories = "/categories";
    }
}