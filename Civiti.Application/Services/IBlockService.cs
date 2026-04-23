using Civiti.Application.Responses.User;

namespace Civiti.Application.Services;

public interface IBlockService
{
    Task<(bool Success, BlockUserResponse? Data, string? Error)> BlockUserAsync(Guid targetUserId, string supabaseUserId);
    Task<(bool Success, string? Error)> UnblockUserAsync(Guid targetUserId, string supabaseUserId);
    Task<(bool Success, List<BlockedUserResponse>? Data, string? Error)> GetBlockedUsersAsync(string supabaseUserId);
}
