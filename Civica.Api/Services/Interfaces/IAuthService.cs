namespace Civica.Api.Services.Interfaces;

/// <summary>
/// Authentication service interface.
/// Profile management has been consolidated to IUserService.
/// </summary>
public interface IAuthService
{
    // Authentication-specific methods can be added here if needed
    // For example: token refresh, logout cleanup, etc.
    // Profile operations are now in IUserService
}
