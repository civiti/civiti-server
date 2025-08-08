using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;

namespace Civica.Api.Services.Interfaces;

public interface IAuthService
{
    Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId);
    Task<UserProfileResponse> CreateUserProfileAsync(CreateUserProfileRequest request, string supabaseUserId, string email);
    Task<UserProfileResponse> UpdateUserProfileAsync(UpdateUserProfileRequest request, string supabaseUserId);
}