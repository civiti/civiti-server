namespace Civica.Api.Infrastructure.Constants;

public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string UserOnly = "UserOnly";
    
    public static class Claims
    {
        public const string Role = "role";
        public const string UserId = "user_id";
        public const string Email = "email";
    }
    
    public static class Roles
    {
        public const string Admin = "admin";
        public const string User = "user";
    }
}