using System.Security.Cryptography;
using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Infrastructure.Exceptions;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Auth;
using Civiti.Api.Models.Responses.Auth;
using Civiti.Api.Models.Responses.Gamification;
using Civiti.Api.Models.Responses.User;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Civiti.Api.Services;

public class UserService(
    ILogger<UserService> logger,
    CivitiDbContext context,
    IGamificationService gamificationService,
    INotificationService notificationService,
    ISupabaseService supabaseService)
    : IUserService
{
    // Note: UserProfile has a global query filter (HasQueryFilter) that automatically
    // excludes IsDeleted rows. Mutation methods use IgnoreQueryFilters() with a single
    // query to distinguish "user not found" from "account deleted" without a second round-trip.
    public async Task<UserGamificationResponse?> GetUserGamificationAsync(string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                logger.LogWarning("User not found for Supabase ID: {SupabaseUserId}", supabaseUserId);
                return null;
            }
            if (user.IsDeleted)
                throw new AccountDeletedException();

            return await BuildGamificationResponseAsync(user);
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not AccountDeletedException)
        {
            logger.LogError(ex, "Error getting user gamification for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to get user gamification for Supabase ID: {supabaseUserId}", ex);
        }
    }

    private async Task<UserGamificationResponse> BuildGamificationResponseAsync(UserProfile user)
    {
        List<BadgeResponse> recentBadges = await gamificationService.GetUserBadgesAsync(user.Id);
        List<AchievementProgressResponse> activeAchievements = await gamificationService.GetUserAchievementsAsync(user.Id);

        var nextLevel = user.Level + 1;
        var currentLevelPoints = GetPointsForLevel(user.Level);
        var nextLevelPoints = GetPointsForLevel(nextLevel);
        var pointsToNextLevel = nextLevelPoints - user.Points;
        var pointsInCurrentLevel = user.Points - currentLevelPoints;
        var levelRange = nextLevelPoints - currentLevelPoints;
        var levelProgressPercentage = levelRange > 0 ? Math.Round((double)pointsInCurrentLevel / levelRange * 100, 2) : 100;

        return new UserGamificationResponse
        {
            Points = user.Points,
            Level = user.Level,
            IssuesReported = user.IssuesReported,
            IssuesResolved = user.IssuesResolved,
            CommunityVotes = user.CommunityVotes,
            CurrentLoginStreak = user.CurrentLoginStreak,
            LongestLoginStreak = user.LongestLoginStreak,
            RecentBadges = recentBadges.Take(5).ToList(),
            ActiveAchievements = activeAchievements.Where(a => !a.Completed).Take(5).ToList(),
            CurrentLevelPoints = currentLevelPoints,
            NextLevelPoints = nextLevelPoints,
            PointsToNextLevel = pointsToNextLevel > 0 ? pointsToNextLevel : 0,
            PointsInCurrentLevel = pointsInCurrentLevel > 0 ? pointsInCurrentLevel : 0,
            LevelProgressPercentage = levelProgressPercentage
        };
    }

    public async Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId)
    {
        try
        {
            // Bypass global filter to distinguish "not found" from "soft-deleted"
            UserProfile? user = await context.UserProfiles
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                logger.LogWarning("User not found for Supabase ID: {SupabaseUserId}", supabaseUserId);
                return null;
            }
            if (user.IsDeleted)
                throw new AccountDeletedException();

            // Track login streak (once per day) and get updated user data in a single operation
            user = await UpdateLoginStreakAsync(user.Id) ?? user;

            UserGamificationResponse gamification = await BuildGamificationResponseAsync(user);

            return new UserProfileResponse
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                PhotoUrl = user.PhotoUrl,
                County = user.County,
                City = user.City,
                District = user.District,
                ResidenceType = user.ResidenceType?.ToString(),
                IssueUpdatesEnabled = user.IssueUpdatesEnabled,
                CommunityNewsEnabled = user.CommunityNewsEnabled,
                MonthlyDigestEnabled = user.MonthlyDigestEnabled,
                AchievementsEnabled = user.AchievementsEnabled,
                Points = user.Points,
                Level = user.Level,
                EmailVerified = user.EmailVerified,
                Gamification = gamification,
                CreatedAt = user.CreatedAt
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not AccountDeletedException)
        {
            logger.LogError(ex, "Error getting user profile for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to get user profile for Supabase ID: {supabaseUserId}", ex);
        }
    }

    public async Task<UserProfileResponse> GetOrCreateUserProfileAsync(
        string supabaseUserId,
        string email,
        string displayName,
        string? photoUrl,
        SignupMetadata? signupMetadata = null)
    {
        // Try to get existing profile first
        UserProfileResponse? existingProfile = await GetUserProfileAsync(supabaseUserId);
        if (existingProfile != null)
        {
            return existingProfile;
        }

        // Block re-creation of soft-deleted accounts.
        // GetUserProfileAsync (above) already uses IgnoreQueryFilters and throws
        // AccountDeleted for deleted rows — so if we reach here, the user truly didn't
        // exist at that point. However, a TOCTOU race is possible: another request could
        // create and soft-delete a profile between the null return above and the INSERT
        // below. This explicit check with IgnoreQueryFilters closes that window.
        bool wasDeleted = await context.UserProfiles
            .IgnoreQueryFilters()
            .AnyAsync(u => u.SupabaseUserId == supabaseUserId && u.IsDeleted);
        if (wasDeleted)
        {
            logger.LogWarning("Blocked profile re-creation for deleted user {SupabaseUserId}", supabaseUserId);
            throw new AccountDeletedException();
        }

        // Profile doesn't exist - attempt to create it
        try
        {
            logger.LogInformation("Auto-creating profile for user {SupabaseUserId}", supabaseUserId);

            UserProfile user = new()
            {
                Id = Guid.NewGuid(),
                SupabaseUserId = supabaseUserId,
                Email = email,
                DisplayName = displayName,
                PhotoUrl = photoUrl,
                County = signupMetadata?.County ?? "București",
                City = signupMetadata?.City ?? "București",
                District = signupMetadata?.District ?? "Sector 5",
                ResidenceType = !string.IsNullOrWhiteSpace(signupMetadata?.ResidenceType) && Enum.TryParse<ResidenceType>(signupMetadata.ResidenceType, ignoreCase: true, out var rt) ? rt : null,
                IssueUpdatesEnabled = signupMetadata?.IssueUpdatesEnabled ?? true,
                CommunityNewsEnabled = signupMetadata?.CommunityNewsEnabled ?? true,
                MonthlyDigestEnabled = signupMetadata?.MonthlyDigestEnabled ?? false,
                AchievementsEnabled = signupMetadata?.AchievementsEnabled ?? true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastActivityDate = DateTime.UtcNow,
                CurrentLoginStreak = 1,
                LongestLoginStreak = 1,
                EmailVerified = true
            };

            context.UserProfiles.Add(user);
            await context.SaveChangesAsync();

            logger.LogInformation("User profile auto-created: {UserId} for Supabase user {SupabaseUserId}",
                user.Id, supabaseUserId);

            return (await GetUserProfileAsync(supabaseUserId))!;
        }
        catch (DbUpdateException ex)
        {
            // Handle race condition: another request may have created the profile concurrently
            // Clear the failed entity from the change tracker and retry the get
            context.ChangeTracker.Clear();

            // Distinguish concurrent soft-delete from concurrent profile creation
            bool deletedDuringRace = await context.UserProfiles
                .IgnoreQueryFilters()
                .AnyAsync(u => u.SupabaseUserId == supabaseUserId && u.IsDeleted);
            if (deletedDuringRace)
                throw new AccountDeletedException();

            logger.LogInformation(
                "Profile creation conflict for {SupabaseUserId}, fetching existing profile (likely concurrent creation)",
                supabaseUserId);

            UserProfileResponse? profile = await GetUserProfileAsync(supabaseUserId);
            if (profile != null)
            {
                return profile;
            }

            // If still null, it's a genuine database error
            logger.LogError(ex, "Database error in GetOrCreateUserProfileAsync for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to get or create user profile for Supabase ID: {supabaseUserId}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not AccountDeletedException)
        {
            logger.LogError(ex, "Error in GetOrCreateUserProfileAsync for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to get or create user profile for Supabase ID: {supabaseUserId}", ex);
        }
    }

    private async Task<UserProfile?> UpdateLoginStreakAsync(Guid userId)
    {
        // Use execution strategy to handle transient failures
        // Re-fetch user inside callback to ensure fresh data on retry
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Clear change tracker to ensure fresh data on retry
            context.ChangeTracker.Clear();

            using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Re-fetch user inside execution strategy to get fresh data on retry.
                // Use IgnoreQueryFilters so a concurrent soft-delete doesn't silently
                // return null and let stale data fall through as 200 OK.
                UserProfile? user = await context.UserProfiles
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    logger.LogWarning("User {UserId} not found for login streak update", userId);
                    return null;
                }
                if (user.IsDeleted)
                    throw new AccountDeletedException();

                DateTime today = DateTime.UtcNow.Date;
                DateTime lastActivityDate = user.LastActivityDate.Date;

                // Skip if already updated today - return current user data
                if (lastActivityDate == today)
                {
                    // Detach entity and return a copy to avoid tracking issues
                    context.Entry(user).State = EntityState.Detached;
                    return user;
                }

                var previousStreak = user.CurrentLoginStreak;

                // Check if this is a consecutive day
                if (lastActivityDate == today.AddDays(-1))
                {
                    // Consecutive day - increment streak
                    user.CurrentLoginStreak++;
                    logger.LogInformation("User {UserId} login streak incremented to {Streak}", user.Id, user.CurrentLoginStreak);
                }
                else
                {
                    // Streak broken - reset to 1
                    user.CurrentLoginStreak = 1;
                    logger.LogInformation("User {UserId} login streak reset to 1 (was {PreviousStreak})", user.Id, previousStreak);
                }

                // Update longest streak if current exceeds it
                if (user.CurrentLoginStreak > user.LongestLoginStreak)
                {
                    user.LongestLoginStreak = user.CurrentLoginStreak;
                }

                // Update last activity date
                user.LastActivityDate = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                // Update achievement progress for login_streak (absolute value, not incremental)
                await gamificationService.UpdateAchievementProgressAsync(
                    user.Id,
                    "login_streak",
                    user.CurrentLoginStreak,
                    isAbsolute: true);

                // Check for badge eligibility
                await gamificationService.CheckAndAwardBadgesAsync(user.Id);

                // Commit only after all operations succeed
                await transaction.CommitAsync();

                // Flush gamification notifications now that the transaction is committed
                await gamificationService.FlushPendingNotificationsAsync();

                // Detach entity and return to avoid tracking issues with subsequent queries
                context.Entry(user).State = EntityState.Detached;
                return user;
            }
            catch (AccountDeletedException)
            {
                await transaction.RollbackAsync();
                logger.LogWarning("Login streak update skipped — user {UserId} was concurrently deleted", userId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "Error updating login streak for user {UserId}", userId);
                throw;
            }
        });
    }

    public async Task<UserProfileResponse> CreateUserProfileAsync(CreateUserProfileRequest request, string supabaseUserId, string email)
    {
        try
        {
            UserProfile? existingUser = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (existingUser is { IsDeleted: true })
            {
                logger.LogWarning("Blocked profile re-creation for deleted user {SupabaseUserId}", supabaseUserId);
                throw new AccountDeletedException();
            }

            if (existingUser != null)
            {
                throw new ArgumentException("User profile already exists");
            }

            UserProfile user = new()
            {
                Id = Guid.NewGuid(),
                SupabaseUserId = supabaseUserId,
                Email = email,
                DisplayName = request.DisplayName,
                PhotoUrl = request.PhotoUrl,
                County = request.County ?? "București",
                City = request.City ?? "București",
                District = request.District ?? "Sector 5",
                ResidenceType = request.ResidenceType,
                IssueUpdatesEnabled = request.IssueUpdatesEnabled ?? true,
                CommunityNewsEnabled = request.CommunityNewsEnabled ?? true,
                MonthlyDigestEnabled = request.MonthlyDigestEnabled ?? false,
                AchievementsEnabled = request.AchievementsEnabled ?? true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastActivityDate = DateTime.UtcNow,
                CurrentLoginStreak = 1, // Count account creation day as day 1
                LongestLoginStreak = 1,
                EmailVerified = true // Supabase handles verification
            };

            context.UserProfiles.Add(user);
            await context.SaveChangesAsync();

            logger.LogInformation("User profile created: {UserId} for Supabase user {SupabaseUserId}",
                user.Id, supabaseUserId);

            // Send welcome email
            try
            {
                await notificationService.NotifyWelcomeAsync(user);
            }
            catch (Exception notifyEx)
            {
                logger.LogError(notifyEx, "Failed to send welcome email for user {UserId}", user.Id);
            }

            // Return full profile with gamification (will be empty for new user)
            return (await GetUserProfileAsync(supabaseUserId))!;
        }
        catch (DbUpdateException)
        {
            // Clear the failed entity from change tracker before re-throwing
            // so callers can safely retry with the same DbContext
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentException and not InvalidOperationException and not AccountDeletedException)
        {
            logger.LogError(ex, "Error creating user profile for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to create user profile for Supabase ID: {supabaseUserId}", ex);
        }
    }

    public async Task<UserProfileResponse> UpdateUserProfileAsync(string supabaseUserId, UpdateUserProfileRequest request)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                logger.LogWarning("User not found for Supabase ID: {SupabaseUserId}", supabaseUserId);
                throw new InvalidOperationException(DomainErrors.UserNotFound);
            }
            if (user.IsDeleted)
                throw new AccountDeletedException();

            // Update only provided fields
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
                user.DisplayName = request.DisplayName;

            if (request.PhotoUrl != null)
                user.PhotoUrl = request.PhotoUrl;

            if (!string.IsNullOrWhiteSpace(request.County))
                user.County = request.County;

            if (!string.IsNullOrWhiteSpace(request.City))
                user.City = request.City;

            if (!string.IsNullOrWhiteSpace(request.District))
                user.District = request.District;

            if (request.ResidenceType.HasValue)
                user.ResidenceType = request.ResidenceType.Value;

            if (request.IssueUpdatesEnabled.HasValue)
                user.IssueUpdatesEnabled = request.IssueUpdatesEnabled.Value;

            if (request.CommunityNewsEnabled.HasValue)
                user.CommunityNewsEnabled = request.CommunityNewsEnabled.Value;

            if (request.MonthlyDigestEnabled.HasValue)
                user.MonthlyDigestEnabled = request.MonthlyDigestEnabled.Value;

            if (request.AchievementsEnabled.HasValue)
                user.AchievementsEnabled = request.AchievementsEnabled.Value;

            user.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            logger.LogInformation("Updated profile for user {UserId}", user.Id);

            return (await GetUserProfileAsync(supabaseUserId))!;
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not AccountDeletedException)
        {
            logger.LogError(ex, "Error updating user profile for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to update user profile for Supabase ID: {supabaseUserId}", ex);
        }
    }

    public async Task<LeaderboardResponse> GetLeaderboardAsync(int page = 1, int pageSize = 50, string period = "all")
    {
        try
        {
            IQueryable<UserProfile> query = context.UserProfiles.AsNoTracking();

            // Apply period filter
            if (period != "all")
            {
                DateTime startDate = period switch
                {
                    "week" => DateTime.UtcNow.AddDays(-7),
                    "month" => DateTime.UtcNow.AddMonths(-1),
                    "year" => DateTime.UtcNow.AddYears(-1),
                    _ => DateTime.MinValue
                };

                if (startDate != DateTime.MinValue)
                {
                    query = query.Where(u => u.LastActivityDate >= startDate);
                }
            }

            // Get top users by points
            var topUsers = await query
                .OrderByDescending(u => u.Points)
                .ThenByDescending(u => u.IssuesResolved)
                .ThenByDescending(u => u.IssuesReported)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.Id,
                    u.DisplayName,
                    u.PhotoUrl,
                    u.City,
                    u.Points,
                    u.Level,
                    u.IssuesReported,
                    u.IssuesResolved
                })
                .ToListAsync();

            var totalEntries = await query.CountAsync();

            // Get recent badges for each user
            List<Guid> userIds = topUsers.Select(u => u.Id).ToList();
            var recentBadges = await context.UserBadges
                .Include(ub => ub.Badge)
                .Where(ub => userIds.Contains(ub.UserId))
                .GroupBy(ub => ub.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    BadgeNames = g.OrderByDescending(ub => ub.EarnedAt)
                        .Take(3)
                        .Select(ub => ub.Badge.Name)
                        .ToList()
                })
                .ToListAsync();

            Dictionary<Guid, List<string>> badgesByUser = recentBadges.ToDictionary(x => x.UserId, x => x.BadgeNames);

            List<LeaderboardEntry> leaderboardEntries = topUsers.Select((user, index) => new LeaderboardEntry
            {
                Rank = (page - 1) * pageSize + index + 1,
                User = new UserInfo
                {
                    Id = user.Id,
                    DisplayName = user.DisplayName,
                    PhotoUrl = user.PhotoUrl,
                    City = user.City
                },
                Points = user.Points,
                Level = user.Level,
                IssuesReported = user.IssuesReported,
                IssuesResolved = user.IssuesResolved,
                RecentBadges = badgesByUser.TryGetValue(user.Id, out List<string>? value) ? value : []
            }).ToList();

            return new LeaderboardResponse
            {
                Leaderboard = leaderboardEntries,
                Period = period,
                Category = "points",
                TotalEntries = totalEntries,
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting leaderboard for period: {Period}", period);
            throw new InvalidOperationException($"Failed to get leaderboard for period: {period}", ex);
        }
    }

    public async Task<DeleteUserResult> DeleteUserAsync(string supabaseUserId)
    {
        try
        {
            // Use execution strategy to handle transient failures during PII scrub.
            // Re-fetch user inside callback to ensure fresh data on retry.
            IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

            bool alreadyDeleted = false;
            Guid? deletedUserId = null;

            await strategy.ExecuteAsync(async () =>
            {
                // Reset closure state on each retry to prevent stale flags from a
                // previous attempt (e.g. SaveChangesAsync succeeded but a later
                // transient failure triggered a retry — without reset the retry
                // would see IsDeleted = true and incorrectly return AlreadyDeleted).
                alreadyDeleted = false;
                deletedUserId = null;

                // Clear change tracker to ensure fresh data on retry
                context.ChangeTracker.Clear();

                UserProfile? user = await context.UserProfiles
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

                if (user == null)
                {
                    logger.LogWarning("User not found for deletion: {SupabaseUserId}", supabaseUserId);
                    return;
                }

                // Already soft-deleted locally — skip DB work, just flag for Supabase retry.
                if (user.IsDeleted)
                {
                    alreadyDeleted = true;
                    return;
                }

                // Explicit transaction ensures all PII scrub fields are committed atomically.
                // Without this, a failure mid-SaveChangesAsync could leave partial PII intact.
                await using var transaction = await context.Database.BeginTransactionAsync();

                // 1. Anonymize PII and soft-delete locally FIRST so the DB is always consistent.
                //    Keep the original SupabaseUserId so the global query filter + the
                //    IgnoreQueryFilters guard in GetOrCreateUserProfileAsync blocks re-creation.
                var opaqueId = Convert.ToHexString(SHA256.HashData(user.Id.ToByteArray()))[..32].ToLowerInvariant();
                user.Email = $"deleted_{opaqueId}@civica.ro";
                user.DisplayName = "Deleted User";
                user.PhotoUrl = null;
                user.Phone = null;
                user.County = "Unknown";
                user.City = "Unknown";
                user.District = "Unknown";
                user.ResidenceType = null;
                // SupabaseUserId intentionally preserved for deleted-user lookup

                // Disable all notification preferences
                user.IssueUpdatesEnabled = false;
                user.CommunityNewsEnabled = false;
                user.MonthlyDigestEnabled = false;
                user.AchievementsEnabled = false;

                // Mark as deleted
                user.IsDeleted = true;
                user.DeletedAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                deletedUserId = user.Id;
            });

            // User was not found in the database
            if (deletedUserId == null && !alreadyDeleted)
                return DeleteUserResult.NotFound;

            // 2. Revoke Supabase auth AFTER the local save succeeds (or was already done).
            //    If this fails, PII is already scrubbed and the soft-delete flag
            //    prevents profile re-creation. The stale auth can be cleaned up later.
            var supabaseDeleted = await supabaseService.DeleteAuthUserAsync(supabaseUserId);
            if (alreadyDeleted)
            {
                logger.LogInformation(
                    "Retried Supabase auth cleanup for previously-deleted user {SupabaseUserId}: auth {AuthResult}",
                    supabaseUserId, supabaseDeleted ? "revoked" : "still pending");
                return DeleteUserResult.AlreadyDeleted;
            }

            if (!supabaseDeleted)
            {
                logger.LogWarning(
                    "Supabase auth deletion failed for user {UserId} after local soft-delete. " +
                    "Auth record requires manual cleanup.", deletedUserId);
            }

            logger.LogInformation("Soft deleted user {UserId} (Supabase auth removed: {SupabaseDeleted})",
                deletedUserId, supabaseDeleted);
            return DeleteUserResult.Deleted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting user for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to delete user for Supabase ID: {supabaseUserId}", ex);
        }
    }

    private static int GetPointsForLevel(int level)
    {
        // Level progression formula: each level requires more points
        // Level 1: 0 points
        // Level 2: 100 points
        // Level 3: 250 points
        // Level 4: 450 points
        // etc.
        if (level <= 1) return 0;
        return (level - 1) * 50 + GetPointsForLevel(level - 1);
    }
}