using Civiti.Api.Services.Interfaces;

namespace Civiti.Api.Services;

/// <summary>
/// Authentication service implementation.
/// Profile management has been consolidated to UserService.
/// </summary>
public class AuthService : IAuthService
{
    // Authentication-specific methods can be added here if needed
    // For example: token refresh, logout cleanup, session management, etc.
    // Profile operations are now handled by UserService
}
