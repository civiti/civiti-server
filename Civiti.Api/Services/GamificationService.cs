using Civiti.Api.Data;
using Civiti.Api.Infrastructure.Localization;
using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Responses.Gamification;
using Civiti.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Api.Services;

public class GamificationService(
    ILogger<GamificationService> logger,
    CivitiDbContext context,
    INotificationService notificationService) : IGamificationService
{
    private readonly List<Func<Task>> _pendingNotifications = [];

    public async Task FlushPendingNotificationsAsync()
    {
        var notifications = _pendingNotifications.ToList();
        _pendingNotifications.Clear();

        foreach (var notify in notifications)
        {
            try
            {
                await notify();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send deferred gamification notification");
            }
        }
    }

    private async Task FlushIfNoTransactionAsync()
    {
        if (context.Database.CurrentTransaction == null)
        {
            await FlushPendingNotificationsAsync();
        }
    }

    public async Task AwardPointsAsync(Guid userId, int points, string reason, bool saveChanges = true)
    {
        try
        {
            UserProfile? user = await context.UserProfiles.FindAsync(userId);
            if (user == null)
            {
                logger.LogWarning("User not found for awarding points: {UserId}", userId);
                return;
            }

            user.Points += points;

            // Update level if needed
            var newLevel = CalculateLevelFromPoints(user.Points);
            if (newLevel > user.Level)
            {
                user.Level = newLevel;
                logger.LogInformation("User {UserId} leveled up to {Level}", userId, newLevel);

                // Award level up achievement (use absolute progress to set level directly)
                await UpdateAchievementProgressAsync(userId, "level_up", newLevel, isAbsolute: true, saveChanges: false);

                var capturedUser = user;
                var capturedLevel = newLevel;
                _pendingNotifications.Add(() => notificationService.NotifyLevelUpAsync(capturedUser, capturedLevel));
            }

            user.UpdatedAt = DateTime.UtcNow;

            if (saveChanges)
            {
                await context.SaveChangesAsync();
                await FlushIfNoTransactionAsync();
            }

            logger.LogInformation("Awarded {Points} points to user {UserId} for {Reason}", points, userId, reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error awarding points to user {UserId}", userId);
            throw;
        }
    }

    public async Task DeductPointsAsync(Guid userId, int points, string reason, bool saveChanges = true)
    {
        try
        {
            UserProfile? user = await context.UserProfiles.FindAsync(userId);
            if (user == null)
            {
                logger.LogWarning("User not found for deducting points: {UserId}", userId);
                return;
            }

            user.Points = Math.Max(0, user.Points - points);

            // Recalculate level based on new points
            var newLevel = CalculateLevelFromPoints(user.Points);
            if (newLevel < user.Level)
            {
                user.Level = newLevel;
                logger.LogInformation("User {UserId} level decreased to {Level} after point deduction", userId, newLevel);

                // Note: Achievement progress is intentionally NOT decreased when level drops.
                // Achievements represent historical accomplishments and should not regress.
            }

            user.UpdatedAt = DateTime.UtcNow;

            if (saveChanges)
            {
                await context.SaveChangesAsync();
            }

            logger.LogInformation("Deducted {Points} points from user {UserId} for {Reason}", points, userId, reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deducting points from user {UserId}", userId);
            throw;
        }
    }

    public async Task CheckAndAwardBadgesAsync(Guid userId, bool saveChanges = true)
    {
        try
        {
            UserProfile? user = await context.UserProfiles
                .Include(u => u.UserBadges)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                logger.LogWarning("User not found for badge check: {UserId}", userId);
                return;
            }

            HashSet<Guid> earnedBadgeIds = user.UserBadges.Select(ub => ub.BadgeId).ToHashSet();
            List<Badge> allBadges = await context.Badges.Where(b => b.IsActive).ToListAsync();

            foreach (Badge badge in allBadges)
            {
                if (earnedBadgeIds.Contains(badge.Id))
                    continue;

                // Check change tracker for badges added by nested calls (e.g., achievement rewards)
                var existingInChangeTracker = context.ChangeTracker
                    .Entries<UserBadge>()
                    .Any(e => e.Entity.UserId == userId &&
                              e.Entity.BadgeId == badge.Id &&
                              e.State == EntityState.Added);

                if (existingInChangeTracker)
                    continue;

                var earned = await CheckBadgeRequirement(user, badge);
                if (earned)
                {
                    UserBadge userBadge = new()
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        BadgeId = badge.Id,
                        EarnedAt = DateTime.UtcNow
                    };

                    context.UserBadges.Add(userBadge);
                    // Update the HashSet to prevent duplicate insertions during nested calls
                    earnedBadgeIds.Add(badge.Id);
                    logger.LogInformation("User {UserId} earned badge {BadgeName}", userId, badge.Name);

                    var capturedBadgeName = badge.Name;
                    _pendingNotifications.Add(() => notificationService.NotifyBadgeEarnedAsync(user, capturedBadgeName));

                    // Award points for earning badge (don't save yet)
                    var badgePoints = badge.Rarity switch
                    {
                        BadgeRarity.Common => 50,
                        BadgeRarity.Uncommon => 100,
                        BadgeRarity.Rare => 200,
                        BadgeRarity.Epic => 500,
                        BadgeRarity.Legendary => 1000,
                        _ => 50
                    };

                    await AwardPointsAsync(userId, badgePoints, $"Earned badge: {badge.Name}", saveChanges: false);
                }
            }

            if (saveChanges)
            {
                await context.SaveChangesAsync();
                await FlushIfNoTransactionAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking badges for user {UserId}", userId);
            throw;
        }
    }

    public async Task CheckAndAwardAchievementsAsync(Guid userId, bool saveChanges = true)
    {
        try
        {
            // Get achievements from database
            List<UserAchievement> userAchievements = await context.UserAchievements
                .Include(ua => ua.Achievement)
                .Where(ua => ua.UserId == userId && !ua.Completed)
                .ToListAsync();

            // Also check change tracker for newly added achievements not yet saved
            HashSet<Guid> achievementIdsInDb = userAchievements.Select(ua => ua.AchievementId).ToHashSet();
            List<UserAchievement> newAchievementsFromTracker = context.ChangeTracker
                .Entries<UserAchievement>()
                .Where(e => e.Entity.UserId == userId &&
                            !e.Entity.Completed &&
                            e.State == EntityState.Added &&
                            !achievementIdsInDb.Contains(e.Entity.AchievementId))
                .Select(e => e.Entity)
                .ToList();

            // Eagerly load Achievement for new entries from tracker
            foreach (UserAchievement ua in newAchievementsFromTracker)
            {
                if (ua.Achievement == null)
                {
                    ua.Achievement = await context.Achievements.FindAsync(ua.AchievementId);
                }
            }

            // Combine both sources
            userAchievements.AddRange(newAchievementsFromTracker.Where(ua => ua.Achievement != null)!);

            foreach (UserAchievement userAchievement in userAchievements)
            {
                // Skip if already completed in memory (handles deferred save scenario where
                // DB query returns row but tracked entity was already marked complete)
                if (userAchievement.Completed)
                    continue;

                if (userAchievement.Progress >= userAchievement.Achievement.MaxProgress)
                {
                    userAchievement.Completed = true;
                    userAchievement.CompletedAt = DateTime.UtcNow;

                    // Award points (don't save yet)
                    await AwardPointsAsync(userId, userAchievement.Achievement.RewardPoints,
                        $"Completed achievement: {userAchievement.Achievement.Title}", saveChanges: false);

                    // Award badge if associated
                    if (userAchievement.Achievement.RewardBadgeId.HasValue)
                    {
                        Guid rewardBadgeId = userAchievement.Achievement.RewardBadgeId.Value;

                        // Check both database AND change tracker for existing badge
                        // (change tracker may have badges added but not yet committed)
                        var existingInDb = await context.UserBadges
                            .AnyAsync(ub => ub.UserId == userId && ub.BadgeId == rewardBadgeId);

                        var existingInChangeTracker = context.ChangeTracker
                            .Entries<UserBadge>()
                            .Any(e => e.Entity.UserId == userId &&
                                      e.Entity.BadgeId == rewardBadgeId &&
                                      e.State == EntityState.Added);

                        if (!existingInDb && !existingInChangeTracker)
                        {
                            UserBadge userBadge = new()
                            {
                                Id = Guid.NewGuid(),
                                UserId = userId,
                                BadgeId = rewardBadgeId,
                                EarnedAt = DateTime.UtcNow
                            };
                            context.UserBadges.Add(userBadge);
                        }
                    }

                    logger.LogInformation("User {UserId} completed achievement {AchievementTitle}",
                        userId, userAchievement.Achievement.Title);

                    var capturedAchievementTitle = userAchievement.Achievement.Title;
                    UserProfile? achiever = await context.UserProfiles.FindAsync(userId);
                    if (achiever != null)
                    {
                        _pendingNotifications.Add(() => notificationService.NotifyAchievementCompletedAsync(achiever, capturedAchievementTitle));
                    }
                }
            }

            if (saveChanges)
            {
                await context.SaveChangesAsync();
                await FlushIfNoTransactionAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking achievements for user {UserId}", userId);
            throw;
        }
    }

    public async Task UpdateAchievementProgressAsync(Guid userId, string achievementType, int progress = 1, bool isAbsolute = false, bool saveChanges = true)
    {
        try
        {
            List<Achievement> achievements = await context.Achievements
                .Where(a => a.AchievementType == achievementType && a.IsActive)
                .ToListAsync();

            foreach (Achievement achievement in achievements)
            {
                // First check the database
                UserAchievement? userAchievement = await context.UserAchievements
                    .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.AchievementId == achievement.Id);

                // Also check the change tracker for achievements added but not yet saved
                if (userAchievement == null)
                {
                    userAchievement = context.ChangeTracker
                        .Entries<UserAchievement>()
                        .Where(e => e.Entity.UserId == userId &&
                                    e.Entity.AchievementId == achievement.Id &&
                                    e.State == EntityState.Added)
                        .Select(e => e.Entity)
                        .FirstOrDefault();
                }

                if (userAchievement == null)
                {
                    userAchievement = new UserAchievement
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        AchievementId = achievement.Id,
                        Progress = 0
                    };
                    context.UserAchievements.Add(userAchievement);
                }

                if (!userAchievement.Completed)
                {
                    // For absolute progress (e.g., streaks), set directly; otherwise add to existing
                    userAchievement.Progress = isAbsolute
                        ? Math.Min(progress, achievement.MaxProgress)
                        : Math.Min(userAchievement.Progress + progress, achievement.MaxProgress);
                }
            }

            // Check achievements (which may award badges/points)
            await CheckAndAwardAchievementsAsync(userId, saveChanges: false);

            if (saveChanges)
            {
                await context.SaveChangesAsync();
                await FlushIfNoTransactionAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating achievement progress for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<BadgeResponse>> GetUserBadgesAsync(Guid userId)
    {
        try
        {
            var userBadgesRaw = await context.UserBadges
                .Include(ub => ub.Badge)
                .Where(ub => ub.UserId == userId)
                .OrderByDescending(ub => ub.EarnedAt)
                .Select(ub => new
                {
                    ub.Badge.Id,
                    ub.Badge.Name,
                    ub.Badge.Description,
                    ub.Badge.IconUrl,
                    ub.Badge.Category,
                    ub.Badge.Rarity,
                    ub.Badge.RequirementDescription,
                    ub.EarnedAt
                })
                .ToListAsync();

            List<BadgeResponse> userBadges = userBadgesRaw.Select(ub => new BadgeResponse
            {
                Id = ub.Id,
                Name = ub.Name,
                Description = ub.Description,
                IconUrl = ub.IconUrl,
                Category = ub.Category.ToString(),
                Rarity = ub.Rarity.ToString(),
                RequirementDescription = ub.RequirementDescription,
                EarnedAt = ub.EarnedAt,
                IsEarned = true,
                NameRo = BadgeLocalization.GetNameRo(ub.Name),
                DescriptionRo = BadgeLocalization.GetDescriptionRo(ub.Description),
                CategoryRo = BadgeLocalization.GetCategoryRo(ub.Category),
                RarityRo = BadgeLocalization.GetRarityRo(ub.Rarity),
                RequirementDescriptionRo = BadgeLocalization.GetRequirementDescriptionRo(ub.RequirementDescription)
            }).ToList();

            return userBadges;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting badges for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<AchievementProgressResponse>> GetUserAchievementsAsync(Guid userId)
    {
        try
        {
            IQueryable<UserAchievement> achievementsQuery = context.UserAchievements
                .Include(ua => ua.Achievement)
                .Where(ua => ua.UserId == userId);

            var achievementsRaw = await achievementsQuery
                .OrderBy(ua => ua.Completed)
                .ThenByDescending(ua => ua.Progress)
                .Select(ua => new
                {
                    ua.Achievement.Id,
                    ua.Achievement.Title,
                    ua.Achievement.Description,
                    ua.Progress,
                    ua.Achievement.MaxProgress,
                    ua.Achievement.RewardPoints,
                    ua.Completed,
                    ua.CompletedAt
                })
                .ToListAsync();

            List<AchievementProgressResponse> userAchievements = achievementsRaw.Select(ua => new AchievementProgressResponse
            {
                Id = ua.Id,
                Title = ua.Title,
                Description = ua.Description,
                Progress = ua.Progress,
                MaxProgress = ua.MaxProgress,
                RewardPoints = ua.RewardPoints,
                Completed = ua.Completed,
                CompletedAt = ua.CompletedAt,
                PercentageComplete = ua.MaxProgress > 0
                    ? Math.Round((decimal)ua.Progress / ua.MaxProgress * 100, 2)
                    : 0,
                TitleRo = AchievementLocalization.GetTitleRo(ua.Title),
                DescriptionRo = AchievementLocalization.GetDescriptionRo(ua.Description)
            }).ToList();

            return userAchievements;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting achievements for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<BadgeResponse>> GetAvailableBadgesAsync(Guid userId)
    {
        try
        {
            List<Guid> earnedBadgeIds = await context.UserBadges
                .Where(ub => ub.UserId == userId)
                .Select(ub => ub.BadgeId)
                .ToListAsync();

            var badgesRaw = await context.Badges
                .Where(b => b.IsActive)
                .OrderBy(b => b.Category)
                .ThenBy(b => b.Rarity)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.Description,
                    b.IconUrl,
                    b.Category,
                    b.Rarity,
                    b.RequirementDescription
                })
                .ToListAsync();

            List<BadgeResponse> badges = badgesRaw.Select(b => new BadgeResponse
            {
                Id = b.Id,
                Name = b.Name,
                Description = b.Description,
                IconUrl = b.IconUrl,
                Category = b.Category.ToString(),
                Rarity = b.Rarity.ToString(),
                RequirementDescription = b.RequirementDescription,
                IsEarned = earnedBadgeIds.Contains(b.Id),
                EarnedAt = null,
                NameRo = BadgeLocalization.GetNameRo(b.Name),
                DescriptionRo = BadgeLocalization.GetDescriptionRo(b.Description),
                CategoryRo = BadgeLocalization.GetCategoryRo(b.Category),
                RarityRo = BadgeLocalization.GetRarityRo(b.Rarity),
                RequirementDescriptionRo = BadgeLocalization.GetRequirementDescriptionRo(b.RequirementDescription)
            }).ToList();

            // Get earned dates for earned badges
            Dictionary<Guid, DateTime> earnedBadges = await context.UserBadges
                .Where(ub => ub.UserId == userId)
                .ToDictionaryAsync(ub => ub.BadgeId, ub => ub.EarnedAt);

            foreach (BadgeResponse badge in badges.Where(b => b.IsEarned))
            {
                if (earnedBadges.TryGetValue(badge.Id, out DateTime earnedAt))
                {
                    badge.EarnedAt = earnedAt;
                }
            }

            return badges;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting available badges for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<BadgeResponse>> GetAllBadgesAsync()
    {
        try
        {
            var badgesRaw = await context.Badges
                .Where(b => b.IsActive)
                .OrderBy(b => b.Category)
                .ThenBy(b => b.Rarity)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.Description,
                    b.IconUrl,
                    b.Category,
                    b.Rarity,
                    b.RequirementDescription
                })
                .ToListAsync();

            List<BadgeResponse> badges = badgesRaw.Select(b => new BadgeResponse
            {
                Id = b.Id,
                Name = b.Name,
                Description = b.Description,
                IconUrl = b.IconUrl,
                Category = b.Category.ToString(),
                Rarity = b.Rarity.ToString(),
                RequirementDescription = b.RequirementDescription,
                IsEarned = false,
                EarnedAt = null,
                NameRo = BadgeLocalization.GetNameRo(b.Name),
                DescriptionRo = BadgeLocalization.GetDescriptionRo(b.Description),
                CategoryRo = BadgeLocalization.GetCategoryRo(b.Category),
                RarityRo = BadgeLocalization.GetRarityRo(b.Rarity),
                RequirementDescriptionRo = BadgeLocalization.GetRequirementDescriptionRo(b.RequirementDescription)
            }).ToList();

            return badges;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all badges");
            throw;
        }
    }

    public async Task<List<AchievementResponse>> GetAllAchievementsAsync()
    {
        try
        {
            var achievementsRaw = await context.Achievements
                .Include(a => a.RewardBadge)
                .Where(a => a.IsActive)
                .OrderBy(a => a.AchievementType)
                .ThenBy(a => a.MaxProgress)
                .Select(a => new
                {
                    a.Id,
                    a.Title,
                    a.Description,
                    a.MaxProgress,
                    a.RewardPoints,
                    a.AchievementType,
                    RewardBadge = a.RewardBadge != null
                        ? new
                        {
                            a.RewardBadge.Id,
                            a.RewardBadge.Name,
                            Description = a.RewardBadge.Description,
                            a.RewardBadge.IconUrl,
                            a.RewardBadge.Category,
                            a.RewardBadge.Rarity,
                            a.RewardBadge.RequirementDescription
                        }
                        : null
                })
                .ToListAsync();

            List<AchievementResponse> achievements = achievementsRaw.Select(a => new AchievementResponse
            {
                Id = a.Id,
                Title = a.Title,
                Description = a.Description,
                MaxProgress = a.MaxProgress,
                RewardPoints = a.RewardPoints,
                AchievementType = a.AchievementType,
                TitleRo = AchievementLocalization.GetTitleRo(a.Title),
                DescriptionRo = AchievementLocalization.GetDescriptionRo(a.Description),
                RewardBadge = a.RewardBadge != null
                    ? new BadgeResponse
                    {
                        Id = a.RewardBadge.Id,
                        Name = a.RewardBadge.Name,
                        Description = a.RewardBadge.Description,
                        IconUrl = a.RewardBadge.IconUrl,
                        Category = a.RewardBadge.Category.ToString(),
                        Rarity = a.RewardBadge.Rarity.ToString(),
                        RequirementDescription = a.RewardBadge.RequirementDescription,
                        IsEarned = false,
                        EarnedAt = null,
                        NameRo = BadgeLocalization.GetNameRo(a.RewardBadge.Name),
                        DescriptionRo = BadgeLocalization.GetDescriptionRo(a.RewardBadge.Description),
                        CategoryRo = BadgeLocalization.GetCategoryRo(a.RewardBadge.Category),
                        RarityRo = BadgeLocalization.GetRarityRo(a.RewardBadge.Rarity),
                        RequirementDescriptionRo = BadgeLocalization.GetRequirementDescriptionRo(a.RewardBadge.RequirementDescription)
                    }
                    : null
            }).ToList();

            return achievements;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all achievements");
            throw;
        }
    }

    public async Task<LeaderboardResponse> GetLeaderboardAsync(string period = "all", string category = "points", int limit = 50)
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

            // Sort by category
            IOrderedQueryable<UserProfile> orderedQuery = category switch
            {
                "issues" => query.OrderByDescending(u => u.IssuesReported),
                "resolved" => query.OrderByDescending(u => u.IssuesResolved),
                "votes" => query.OrderByDescending(u => u.CommunityVotes),
                _ => query.OrderByDescending(u => u.Points)
            };

            // Get top users
            var topUsers = await orderedQuery
                .ThenByDescending(u => u.Points)
                .Take(limit)
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
                Rank = index + 1,
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
                RecentBadges = badgesByUser.ContainsKey(user.Id) ? badgesByUser[user.Id] : []
            }).ToList();

            return new LeaderboardResponse
            {
                Leaderboard = leaderboardEntries,
                Period = period,
                Category = category,
                TotalEntries = await query.CountAsync(),
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting leaderboard");
            throw;
        }
    }

    private async Task<bool> CheckBadgeRequirement(UserProfile user, Badge badge)
    {
        if (string.IsNullOrEmpty(badge.RequirementType) || !badge.RequirementValue.HasValue)
            return false;

        var requirementMet = badge.RequirementType switch
        {
            "issues_reported" => user.IssuesReported >= badge.RequirementValue,
            "issues_resolved" => user.IssuesResolved >= badge.RequirementValue,
            "community_votes" => user.CommunityVotes >= badge.RequirementValue,
            "quality_photos" => await CheckQualityPhotos(user.Id, badge.RequirementValue.Value),
            "login_streak" => user.CurrentLoginStreak >= badge.RequirementValue,
            "level" => user.Level >= badge.RequirementValue,
            _ => false
        };

        return requirementMet;
    }

    private async Task<bool> CheckQualityPhotos(Guid userId, int requiredCount)
    {
        // For now, count issues with 3+ photos as "quality photos"
        var qualityPhotoCount = await context.Issues
            .Where(i => i.UserId == userId && i.Photos.Count >= 3)
            .CountAsync();

        return qualityPhotoCount >= requiredCount;
    }

    private static int CalculateLevelFromPoints(int points)
    {
        // Inverse of the level points calculation
        int level = 1;
        int requiredPoints = 0;

        while (requiredPoints <= points)
        {
            level++;
            requiredPoints = GetPointsForLevel(level);
        }

        return level - 1;
    }

    private static int GetPointsForLevel(int level)
    {
        if (level <= 1) return 0;
        return (level - 1) * 50 + GetPointsForLevel(level - 1);
    }
}