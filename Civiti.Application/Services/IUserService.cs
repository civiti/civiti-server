using Civiti.Application.Requests.Auth;
using Civiti.Domain.Entities;
using Civiti.Application.Responses.Auth;
using Civiti.Application.Responses.Gamification;
using Civiti.Application.Responses.User;

namespace Civiti.Application.Services;

public interface IUserService
{
    Task<Guid?> GetUserIdAsync(string supabaseUserId);
    Task<UserGamificationResponse?> GetUserGamificationAsync(string supabaseUserId);
    Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId);
    Task<UserProfileResponse> GetOrCreateUserProfileAsync(string supabaseUserId, string email, string displayName, string? photoUrl, SignupMetadata? signupMetadata = null);
    Task<UserProfileResponse> CreateUserProfileAsync(CreateUserProfileRequest request, string supabaseUserId, string email);
    Task<UserProfileResponse> UpdateUserProfileAsync(string supabaseUserId, UpdateUserProfileRequest request);
    Task<LeaderboardResponse> GetLeaderboardAsync(int page = 1, int pageSize = 50, string period = "all");
    /// <summary>
    /// Permanently scrubs PII and soft-deletes the user. Runs with CancellationToken.None
    /// so the operation completes even if the HTTP request is aborted.
    /// </summary>
    Task<DeleteUserResult> DeleteUserAsync(string supabaseUserId);
}
