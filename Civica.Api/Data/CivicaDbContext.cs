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

        // Seed data
        SeedBadges(modelBuilder);
        SeedAchievements(modelBuilder);
        SeedAuthorities(modelBuilder);
    }

    private static void SeedBadges(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Badge>().HasData(
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
                CreatedAt = DateTime.UtcNow
            },
            new Badge
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Picture Perfect",
                Description = "Uploaded high-quality photos with your report",
                IconUrl = "/assets/badges/picture-perfect.svg",
                Category = BadgeCategory.Progress,
                Rarity = BadgeRarity.Uncommon,
                RequirementType = "quality_photos",
                RequirementValue = 3,
                RequirementDescription = "Upload 3 high-quality photos",
                CreatedAt = DateTime.UtcNow
            },
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
                CreatedAt = DateTime.UtcNow
            },
            new Badge
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Name = "Problem Solver",
                Description = "3 of your issues have been resolved",
                IconUrl = "/assets/badges/problem-solver.svg",
                Category = BadgeCategory.Achievement,
                Rarity = BadgeRarity.Rare,
                RequirementType = "issues_resolved",
                RequirementValue = 3,
                RequirementDescription = "3 issues resolved",
                CreatedAt = DateTime.UtcNow
            }
        );
    }

    private static void SeedAchievements(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Achievement>().HasData(
            new Achievement
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "First Steps",
                Description = "Report your first issue",
                MaxProgress = 1,
                RewardPoints = 50,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 1}",
                CreatedAt = DateTime.UtcNow
            },
            new Achievement
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Title = "Community Champion",
                Description = "Report 10 issues",
                MaxProgress = 10,
                RewardPoints = 200,
                RewardBadgeId = null,
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 10}",
                CreatedAt = DateTime.UtcNow
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
                IsActive = true,
                CreatedAt = createdAt
            },
            // Bucharest Sectors
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000002"),
                Name = "Primăria Sectorului 1 București",
                Email = "primarie@primarias1.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000003"),
                Name = "Primăria Sectorului 2 București",
                Email = "primarie@ps2.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000004"),
                Name = "Primăria Sectorului 3 București",
                Email = "primarie@primarie3.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000005"),
                Name = "Primăria Sectorului 4 București",
                Email = "primarie@ps4.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000006"),
                Name = "Primăria Sectorului 5 București",
                Email = "primarie@sector5.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000001-0000-0000-0000-000000000007"),
                Name = "Primăria Sectorului 6 București",
                Email = "primarie@primarie6.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            // Major Romanian Cities
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000001"),
                Name = "Primăria Municipiului Cluj-Napoca",
                Email = "primarie@primariaclujnapoca.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000002"),
                Name = "Primăria Municipiului Timișoara",
                Email = "primarie@primariatm.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000003"),
                Name = "Primăria Municipiului Iași",
                Email = "primarie@primaria-iasi.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000004"),
                Name = "Primăria Municipiului Constanța",
                Email = "primarie@primaria-constanta.ro",
                IsActive = true,
                CreatedAt = createdAt
            },
            new Authority
            {
                Id = Guid.Parse("a0000002-0000-0000-0000-000000000005"),
                Name = "Primăria Municipiului Brașov",
                Email = "primarie@brasovcity.ro",
                IsActive = true,
                CreatedAt = createdAt
            }
        );
    }
}