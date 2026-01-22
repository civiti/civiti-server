using Microsoft.EntityFrameworkCore;
using Civica.Api.Models.Domain;
using Civica.Api.Data.Configurations;

namespace Civica.Api.Data;

public class CivicaDbContext(DbContextOptions<CivicaDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles { get; set; } = null!;
    public DbSet<Issue> Issues { get; set; } = null!;
    public DbSet<IssuePhoto> IssuePhotos { get; set; } = null!;
    public DbSet<Badge> Badges { get; set; } = null!;
    public DbSet<Achievement> Achievements { get; set; } = null!;
    public DbSet<UserBadge> UserBadges { get; set; } = null!;
    public DbSet<UserAchievement> UserAchievements { get; set; } = null!;
    public DbSet<AdminAction> AdminActions { get; set; } = null!;
    public DbSet<Authority> Authorities { get; set; } = null!;
    public DbSet<IssueAuthority> IssueAuthorities { get; set; } = null!;
    public DbSet<Activity> Activities { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations
        modelBuilder.ApplyConfiguration(new UserProfileConfiguration());
        modelBuilder.ApplyConfiguration(new IssueConfiguration());
        modelBuilder.ApplyConfiguration(new IssuePhotoConfiguration());
        modelBuilder.ApplyConfiguration(new BadgeConfiguration());
        modelBuilder.ApplyConfiguration(new AchievementConfiguration());
        modelBuilder.ApplyConfiguration(new UserBadgeConfiguration());
        modelBuilder.ApplyConfiguration(new UserAchievementConfiguration());
        modelBuilder.ApplyConfiguration(new AdminActionConfiguration());
        modelBuilder.ApplyConfiguration(new AuthorityConfiguration());
        modelBuilder.ApplyConfiguration(new IssueAuthorityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityConfiguration());

        // Seed data
        SeedBadges(modelBuilder);
        SeedAchievements(modelBuilder);
        SeedAuthorities(modelBuilder);
    }

    private static void SeedBadges(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Badge>().HasData(
            // === ISSUES REPORTED PROGRESSION ===
            new Badge
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Civic Starter",
                Description = "Reported your first community issue",
                IconUrl = "/assets/badges/civic-starter.svg",
                Category = BadgeCategory.Starter,
                Rarity = BadgeRarity.Common,
                RequirementType = "issues_reported",
                RequirementValue = 1,
                RequirementDescription = "Report your first issue",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111112"),
                Name = "Active Citizen",
                Description = "Reported 5 community issues",
                IconUrl = "/assets/badges/active-citizen.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Uncommon,
                RequirementType = "issues_reported",
                RequirementValue = 5,
                RequirementDescription = "Report 5 issues",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111113"),
                Name = "Community Champion",
                Description = "Reported 15 community issues",
                IconUrl = "/assets/badges/community-champion.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Rare,
                RequirementType = "issues_reported",
                RequirementValue = 15,
                RequirementDescription = "Report 15 issues",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111114"),
                Name = "Civic Leader",
                Description = "Reported 30 community issues - a true leader!",
                IconUrl = "/assets/badges/civic-leader.svg",
                Category = BadgeCategory.Achievement,
                Rarity = BadgeRarity.Epic,
                RequirementType = "issues_reported",
                RequirementValue = 30,
                RequirementDescription = "Report 30 issues",
                CreatedAt = seedDate
            },

            // === ISSUES RESOLVED PROGRESSION ===
            new Badge
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Name = "Problem Solver",
                Description = "Had your first issue resolved",
                IconUrl = "/assets/badges/problem-solver.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Uncommon,
                RequirementType = "issues_resolved",
                RequirementValue = 1,
                RequirementDescription = "1 issue resolved",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555556"),
                Name = "Resolution Expert",
                Description = "Had 5 of your issues resolved",
                IconUrl = "/assets/badges/resolution-expert.svg",
                Category = BadgeCategory.Achievement,
                Rarity = BadgeRarity.Rare,
                RequirementType = "issues_resolved",
                RequirementValue = 5,
                RequirementDescription = "5 issues resolved",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555557"),
                Name = "Master Resolver",
                Description = "Had 10 of your issues resolved - making real impact!",
                IconUrl = "/assets/badges/master-resolver.svg",
                Category = BadgeCategory.Achievement,
                Rarity = BadgeRarity.Epic,
                RequirementType = "issues_resolved",
                RequirementValue = 10,
                RequirementDescription = "10 issues resolved",
                CreatedAt = seedDate
            },

            // === QUALITY PHOTOS PROGRESSION ===
            new Badge
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Picture Perfect",
                Description = "Uploaded high-quality photos with your reports",
                IconUrl = "/assets/badges/picture-perfect.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Uncommon,
                RequirementType = "quality_photos",
                RequirementValue = 3,
                RequirementDescription = "Upload 3 issues with quality photos",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222223"),
                Name = "Photo Journalist",
                Description = "Documented 10 issues with quality photos",
                IconUrl = "/assets/badges/photo-journalist.svg",
                Category = BadgeCategory.Achievement,
                Rarity = BadgeRarity.Rare,
                RequirementType = "quality_photos",
                RequirementValue = 10,
                RequirementDescription = "Upload 10 issues with quality photos",
                CreatedAt = seedDate
            },

            // === COMMUNITY VOTES (for future) ===
            new Badge
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Community Voice",
                Description = "Voted on 10 community issues",
                IconUrl = "/assets/badges/community-voice.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Common,
                RequirementType = "community_votes",
                RequirementValue = 10,
                RequirementDescription = "Vote on 10 issues",
                CreatedAt = seedDate
            },

            // === LOGIN STREAK PROGRESSION ===
            new Badge
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Name = "Dedicated Citizen",
                Description = "Logged in 7 days in a row",
                IconUrl = "/assets/badges/dedicated-citizen.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Uncommon,
                RequirementType = "login_streak",
                RequirementValue = 7,
                RequirementDescription = "7-day login streak",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666667"),
                Name = "Consistency King",
                Description = "Logged in 30 days in a row - incredible dedication!",
                IconUrl = "/assets/badges/consistency-king.svg",
                Category = BadgeCategory.Achievement,
                Rarity = BadgeRarity.Rare,
                RequirementType = "login_streak",
                RequirementValue = 30,
                RequirementDescription = "30-day login streak",
                CreatedAt = seedDate
            },

            // === LEVEL PROGRESSION ===
            new Badge
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                Name = "Rising Star",
                Description = "Reached level 5",
                IconUrl = "/assets/badges/rising-star.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Uncommon,
                RequirementType = "level",
                RequirementValue = 5,
                RequirementDescription = "Reach level 5",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777778"),
                Name = "Veteran",
                Description = "Reached level 10 - a seasoned civic advocate",
                IconUrl = "/assets/badges/veteran.svg",
                Category = BadgeCategory.Achievement,
                Rarity = BadgeRarity.Rare,
                RequirementType = "level",
                RequirementValue = 10,
                RequirementDescription = "Reach level 10",
                CreatedAt = seedDate
            },
            new Badge
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777779"),
                Name = "Legend",
                Description = "Reached level 20 - a true civic legend!",
                IconUrl = "/assets/badges/legend.svg",
                Category = BadgeCategory.Special,
                Rarity = BadgeRarity.Legendary,
                RequirementType = "level",
                RequirementValue = 20,
                RequirementDescription = "Reach level 20",
                CreatedAt = seedDate
            }
        );
    }

    private static void SeedAchievements(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Achievement>().HasData(
            // === ISSUES REPORTED ACHIEVEMENTS ===
            new Achievement
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "First Steps",
                Description = "Report your first issue to start making a difference",
                MaxProgress = 1,
                RewardPoints = 25,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), // Civic Starter
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 1}",
                CreatedAt = seedDate
            },
            new Achievement
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab"),
                Title = "Getting Started",
                Description = "Report 5 issues and become an active citizen",
                MaxProgress = 5,
                RewardPoints = 75,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111112"), // Active Citizen
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 5}",
                CreatedAt = seedDate
            },
            new Achievement
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaac"),
                Title = "Community Champion",
                Description = "Report 15 issues to become a community champion",
                MaxProgress = 15,
                RewardPoints = 150,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111113"), // Community Champion badge
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 15}",
                CreatedAt = seedDate
            },
            new Achievement
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaad"),
                Title = "Civic Leadership",
                Description = "Report 30 issues and become a civic leader",
                MaxProgress = 30,
                RewardPoints = 300,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111114"), // Civic Leader
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 30}",
                CreatedAt = seedDate
            },

            // === ISSUES RESOLVED ACHIEVEMENTS ===
            new Achievement
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Title = "First Resolution",
                Description = "Get your first issue resolved",
                MaxProgress = 1,
                RewardPoints = 50,
                RewardBadgeId = Guid.Parse("55555555-5555-5555-5555-555555555555"), // Problem Solver
                AchievementType = "issues_resolved",
                RequirementData = "{\"target\": 1}",
                CreatedAt = seedDate
            },
            new Achievement
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbc"),
                Title = "Resolution Streak",
                Description = "Get 5 of your issues resolved",
                MaxProgress = 5,
                RewardPoints = 150,
                RewardBadgeId = Guid.Parse("55555555-5555-5555-5555-555555555556"), // Resolution Expert
                AchievementType = "issues_resolved",
                RequirementData = "{\"target\": 5}",
                CreatedAt = seedDate
            },

            // === LOGIN STREAK ACHIEVEMENTS ===
            new Achievement
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Title = "Week Warrior",
                Description = "Log in 7 days in a row",
                MaxProgress = 7,
                RewardPoints = 75,
                RewardBadgeId = Guid.Parse("66666666-6666-6666-6666-666666666666"), // Dedicated Citizen
                AchievementType = "login_streak",
                RequirementData = "{\"target\": 7}",
                CreatedAt = seedDate
            },
            new Achievement
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccd"),
                Title = "Monthly Dedication",
                Description = "Log in 30 days in a row",
                MaxProgress = 30,
                RewardPoints = 200,
                RewardBadgeId = Guid.Parse("66666666-6666-6666-6666-666666666667"), // Consistency King
                AchievementType = "login_streak",
                RequirementData = "{\"target\": 30}",
                CreatedAt = seedDate
            },

            // === QUALITY PHOTOS ACHIEVEMENT ===
            new Achievement
            {
                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Title = "Photography Basics",
                Description = "Submit 3 issues with quality photos",
                MaxProgress = 3,
                RewardPoints = 50,
                RewardBadgeId = Guid.Parse("22222222-2222-2222-2222-222222222222"), // Picture Perfect
                AchievementType = "quality_photos",
                RequirementData = "{\"target\": 3}",
                CreatedAt = seedDate
            },

            // === LEVEL ACHIEVEMENTS ===
            new Achievement
            {
                Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                Title = "Level Up!",
                Description = "Reach level 5",
                MaxProgress = 5,
                RewardPoints = 100,
                RewardBadgeId = Guid.Parse("77777777-7777-7777-7777-777777777777"), // Rising Star
                AchievementType = "level_up",
                RequirementData = "{\"target\": 5}",
                CreatedAt = seedDate
            }
        );
    }

    private static void SeedAuthorities(ModelBuilder modelBuilder)
    {
        var createdAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Authority>().HasData(
            // Bucharest General
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000001"),
                Name = "Primăria Municipiului București",
                Email = "pmb@pmb.ro",
                County = "București",
                City = "București",
                District = null, // City-wide authority
                IsActive = true,
                CreatedAt = createdAt
            },
            // Bucharest Sectors
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000002"),
                Name = "Primăria Sectorului 1 București",
                Email = "primarie@primarias1.ro",
                County = "București",
                City = "București",
                District = "Sector 1",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000003"),
                Name = "Primăria Sectorului 2 București",
                Email = "primarie@ps2.ro",
                County = "București",
                City = "București",
                District = "Sector 2",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000004"),
                Name = "Primăria Sectorului 3 București",
                Email = "primarie@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000005"),
                Name = "Primăria Sectorului 4 București",
                Email = "primarie@ps4.ro",
                County = "București",
                City = "București",
                District = "Sector 4",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000006"),
                Name = "Primăria Sectorului 5 București",
                Email = "primarie@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000007"),
                Name = "Primăria Sectorului 6 București",
                Email = "primarie@primarie6.ro",
                County = "București",
                City = "București",
                District = "Sector 6",
                IsActive = true,
                CreatedAt = createdAt
            },
            // Major Romanian Cities
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000001"),
                Name = "Primăria Municipiului Cluj-Napoca",
                Email = "primarie@primariaclujnapoca.ro",
                County = "Cluj",
                City = "Cluj-Napoca",
                District = null,
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000002"),
                Name = "Primăria Municipiului Timișoara",
                Email = "primarie@primariatm.ro",
                County = "Timiș",
                City = "Timișoara",
                District = null,
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000003"),
                Name = "Primăria Municipiului Iași",
                Email = "primarie@primaria-iasi.ro",
                County = "Iași",
                City = "Iași",
                District = null,
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000004"),
                Name = "Primăria Municipiului Constanța",
                Email = "primarie@primaria-constanta.ro",
                County = "Constanța",
                City = "Constanța",
                District = null,
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000005"),
                Name = "Primăria Municipiului Brașov",
                Email = "primarie@brasovcity.ro",
                County = "Brașov",
                City = "Brașov",
                District = null,
                IsActive = true,
                CreatedAt = createdAt
            }
        );
    }
}