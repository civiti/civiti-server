using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Models.Responses.User;
using Civica.Api.Models.Responses.Gamification;

namespace Civica.Api.Services.Interfaces;

public interface IUserService
{
    Task<UserGamificationResponse> GetUserGamificationAsync(string supabaseUserId);
    Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId);
    Task<UserProfileResponse> UpdateUserProfileAsync(string supabaseUserId, UpdateUserProfileRequest request);
    Task<LeaderboardResponse> GetLeaderboardAsync(int page = 1, int pageSize = 50, string period = "all");
    Task<bool> DeleteUserAsync(string supabaseUserId);
}