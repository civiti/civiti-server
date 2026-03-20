using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Exceptions;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Responses.User;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Api.Services;

public class BlockService(
    ILogger<BlockService> logger,
    CivitiDbContext context) : IBlockService
{
    public async Task<(bool Success, BlockUserResponse? Data, string? Error)> BlockUserAsync(
        Guid targetUserId,
        string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
                return (false, null, DomainErrors.UserNotFound);
            if (user.IsDeleted)
                throw new AccountDeletedException();

            if (user.Id == targetUserId)
                return (false, null, DomainErrors.CannotBlockSelf);

            // Verify target user exists
            var targetExists = await context.UserProfiles
                .AnyAsync(u => u.Id == targetUserId);

            if (!targetExists)
                return (false, null, DomainErrors.TargetUserNotFound);

            // Check if already blocked
            var alreadyBlocked = await context.BlockedUsers
                .AnyAsync(b => b.UserId == user.Id && b.BlockedUserId == targetUserId);

            if (alreadyBlocked)
                return (false, null, DomainErrors.AlreadyBlocked);

            var now = DateTime.UtcNow;
            BlockedUser blocked = new()
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                BlockedUserId = targetUserId,
                CreatedAt = now
            };

            context.BlockedUsers.Add(blocked);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "User {UserId} blocked user {BlockedUserId}",
                user.Id, targetUserId);

            return (true, new BlockUserResponse
            {
                BlockedUserId = targetUserId,
                BlockedAt = now
            }, null);
        }
        catch (AccountDeletedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error blocking user: {TargetUserId}", targetUserId);
            throw;
        }
    }

    public async Task<(bool Success, string? Error)> UnblockUserAsync(
        Guid targetUserId,
        string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
                return (false, DomainErrors.UserNotFound);
            if (user.IsDeleted)
                throw new AccountDeletedException();

            var rowsDeleted = await context.BlockedUsers
                .Where(b => b.UserId == user.Id && b.BlockedUserId == targetUserId)
                .ExecuteDeleteAsync();

            if (rowsDeleted == 0)
                return (false, DomainErrors.UserNotBlocked);

            logger.LogInformation(
                "User {UserId} unblocked user {BlockedUserId}",
                user.Id, targetUserId);

            return (true, null);
        }
        catch (AccountDeletedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unblocking user: {TargetUserId}", targetUserId);
            throw;
        }
    }

    public async Task<(bool Success, List<BlockedUserResponse>? Data, string? Error)> GetBlockedUsersAsync(
        string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
                return (false, null, DomainErrors.UserNotFound);
            if (user.IsDeleted)
                throw new AccountDeletedException();

            List<BlockedUserResponse> blockedUsers = await context.BlockedUsers
                .Where(b => b.UserId == user.Id)
                .OrderByDescending(b => b.CreatedAt)
                .Take(500)
                .Select(b => new BlockedUserResponse
                {
                    UserId = b.BlockedUserId,
                    DisplayName = b.Blocked != null ? b.Blocked.DisplayName : "Deleted User",
                    PhotoUrl = b.Blocked != null ? b.Blocked.PhotoUrl : null,
                    BlockedAt = b.CreatedAt
                })
                .ToListAsync();

            return (true, blockedUsers, null);
        }
        catch (AccountDeletedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting blocked users for: {SupabaseUserId}", supabaseUserId);
            throw;
        }
    }
}
