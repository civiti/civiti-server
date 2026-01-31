using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Models.Requests.Auth;
using Civiti.Api.Models.Responses.Auth;
using Civiti.Api.Models.Responses.Gamification;
using Civiti.Api.Models.Responses.User;

namespace Civiti.Api.Services.Interfaces;

public interface IUserService
{
    Task<UserGamificationResponse> GetUserGamificationAsync(string supabaseUserId);
    Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId);
    Task<UserProfileResponse> GetOrCreateUserProfileAsync(string supabaseUserId, string email, string displayName, string? photoUrl, SignupMetadata? signupMetadata = null);
    Task<UserProfileResponse> CreateUserProfileAsync(CreateUserProfileRequest request, string supabaseUserId, string email);
    Task<UserProfileResponse> UpdateUserProfileAsync(string supabaseUserId, UpdateUserProfileRequest request);
    Task<LeaderboardResponse> GetLeaderboardAsync(int page = 1, int pageSize = 50, string period = "all");
    Task<bool> DeleteUserAsync(string supabaseUserId);
}