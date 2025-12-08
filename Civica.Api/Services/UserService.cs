using Civica.Api.Services.Interfaces;
using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Models.Responses.User;
using Civica.Api.Models.Responses.Gamification;
using Civica.Api.Models.Domain;
using Civica.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Civica.Api.Services;

public class UserService(
    ILogger<UserService> logger,
    CivicaDbContext context,
    IGamificationService gamificationService)
    : IUserService
{
    public async Task<UserGamificationResponse> GetUserGamificationAsync(string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                logger.LogWarning("User not found for Supabase ID: {SupabaseUserId}", supabaseUserId);
                return new UserGamificationResponse();
            }

            // Get recent badges
            List<BadgeResponse> recentBadges = await gamificationService.GetUserBadgesAsync(user.Id);
            List<AchievementProgressResponse> activeAchievements = await gamificationService.GetUserAchievementsAsync(user.Id);

            // Calculate level points
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting user gamification for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to get user gamification for Supabase ID: {supabaseUserId}", ex);
        }
    }

    public async Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                logger.LogWarning("User not found for Supabase ID: {SupabaseUserId}", supabaseUserId);
                return null;
            }

            UserGamificationResponse gamification = await GetUserGamificationAsync(supabaseUserId);

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting user profile for Supabase ID: {SupabaseUserId}", supabaseUserId);
            throw new InvalidOperationException($"Failed to get user profile for Supabase ID: {supabaseUserId}", ex);
        }
    }

    public async Task<UserProfileResponse> CreateUserProfileAsync(CreateUserProfileRequest request, string supabaseUserId, string email)
    {
        try
        {
            UserProfile? existingUser = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                EmailVerified = true // Supabase handles verification
            };

            context.UserProfiles.Add(user);
            await context.SaveChangesAsync();

            logger.LogInformation("User profile created: {UserId} for Supabase user {SupabaseUserId}",
                user.Id, supabaseUserId);

            // Return full profile with gamification (will be empty for new user)
            return (await GetUserProfileAsync(supabaseUserId))!;
        }
        catch (ArgumentException)
        {
            throw; // Re-throw argument exceptions as-is
        }
        catch (Exception ex)
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
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                logger.LogWarning("User not found for Supabase ID: {SupabaseUserId}", supabaseUserId);
                throw new InvalidOperationException("User not found");
            }

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
        catch (Exception ex)
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

    public async Task<bool> DeleteUserAsync(string supabaseUserId)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

            if (user == null)
            {
                logger.LogWarning("User not found for deletion: {SupabaseUserId}", supabaseUserId);
                return false;
            }

            // Soft-delete - anonymize the data
            user.Email = $"deleted_{user.Id}@civica.ro";
            user.DisplayName = "Deleted User";
            user.PhotoUrl = null;
            user.County = "Unknown";
            user.City = "Unknown";
            user.District = "Unknown";
            user.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            logger.LogInformation("Soft deleted user {UserId}", user.Id);
            return true;
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