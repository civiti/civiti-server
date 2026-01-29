namespace Civica.Api.Infrastructure.Constants;

public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string UserOnly = "UserOnly";

    public static class Roles
    {
        public const string Admin = "admin";
        public const string User = "user";
    }
}