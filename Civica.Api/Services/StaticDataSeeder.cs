using Civica.Api.Data;
using Civica.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Civica.Api.Services;

/// <summary>
/// Seeds static reference data (badges, achievements, authorities) at application startup.
/// Idempotent (checks for existing data before inserting).
/// </summary>
public class StaticDataSeeder : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StaticDataSeeder> _logger;

    public StaticDataSeeder(
        IServiceProvider serviceProvider,
        ILogger<StaticDataSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting static data seeding");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();

            await SeedBadgesAsync(context, cancellationToken);
            await SeedAchievementsAsync(context, cancellationToken);
            await SeedAuthoritiesAsync(context, cancellationToken);

            _logger.LogInformation("Static data seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding static data");
            throw; // Static data is required - fail startup if seeding fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedBadgesAsync(CivicaDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Badges.AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Badges already seeded - skipping");
            return;
        }

        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var badges = new List<Badge>
        {
            // === ISSUES REPORTED PROGRESSION ===
            new()
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
            new()
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
            new()
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
            new()
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
            new()
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
            new()
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
            new()
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
            new()
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
            new()
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

            // === COMMUNITY VOTES ===
            new()
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
            new()
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
            new()
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
            new()
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
            new()
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
            new()
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
        };

        context.Badges.AddRange(badges);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} badges", badges.Count);
    }

    private async Task SeedAchievementsAsync(CivicaDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Achievements.AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Achievements already seeded - skipping");
            return;
        }

        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var achievements = new List<Achievement>
        {
            // === ISSUES REPORTED ACHIEVEMENTS ===
            new()
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "First Steps",
                Description = "Report your first issue to start making a difference",
                MaxProgress = 1,
                RewardPoints = 25,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 1}",
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab"),
                Title = "Getting Started",
                Description = "Report 5 issues and become an active citizen",
                MaxProgress = 5,
                RewardPoints = 75,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111112"),
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 5}",
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaac"),
                Title = "Community Champion",
                Description = "Report 15 issues to become a community champion",
                MaxProgress = 15,
                RewardPoints = 150,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111113"),
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 15}",
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaad"),
                Title = "Civic Leadership",
                Description = "Report 30 issues and become a civic leader",
                MaxProgress = 30,
                RewardPoints = 300,
                RewardBadgeId = Guid.Parse("11111111-1111-1111-1111-111111111114"),
                AchievementType = "issues_reported",
                RequirementData = "{\"target\": 30}",
                CreatedAt = seedDate
            },

            // === ISSUES RESOLVED ACHIEVEMENTS ===
            new()
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Title = "First Resolution",
                Description = "Get your first issue resolved",
                MaxProgress = 1,
                RewardPoints = 50,
                RewardBadgeId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                AchievementType = "issues_resolved",
                RequirementData = "{\"target\": 1}",
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbc"),
                Title = "Resolution Streak",
                Description = "Get 5 of your issues resolved",
                MaxProgress = 5,
                RewardPoints = 150,
                RewardBadgeId = Guid.Parse("55555555-5555-5555-5555-555555555556"),
                AchievementType = "issues_resolved",
                RequirementData = "{\"target\": 5}",
                CreatedAt = seedDate
            },

            // === LOGIN STREAK ACHIEVEMENTS ===
            new()
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Title = "Week Warrior",
                Description = "Log in 7 days in a row",
                MaxProgress = 7,
                RewardPoints = 75,
                RewardBadgeId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                AchievementType = "login_streak",
                RequirementData = "{\"target\": 7}",
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccd"),
                Title = "Monthly Dedication",
                Description = "Log in 30 days in a row",
                MaxProgress = 30,
                RewardPoints = 200,
                RewardBadgeId = Guid.Parse("66666666-6666-6666-6666-666666666667"),
                AchievementType = "login_streak",
                RequirementData = "{\"target\": 30}",
                CreatedAt = seedDate
            },

            // === QUALITY PHOTOS ACHIEVEMENT ===
            new()
            {
                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Title = "Photography Basics",
                Description = "Submit 3 issues with quality photos",
                MaxProgress = 3,
                RewardPoints = 50,
                RewardBadgeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                AchievementType = "quality_photos",
                RequirementData = "{\"target\": 3}",
                CreatedAt = seedDate
            },

            // === LEVEL ACHIEVEMENTS ===
            new()
            {
                Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                Title = "Level Up!",
                Description = "Reach level 5",
                MaxProgress = 5,
                RewardPoints = 100,
                RewardBadgeId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                AchievementType = "level_up",
                RequirementData = "{\"target\": 5}",
                CreatedAt = seedDate
            }
        };

        context.Achievements.AddRange(achievements);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} achievements", achievements.Count);
    }

    private async Task SeedAuthoritiesAsync(CivicaDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Authorities.AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Authorities already seeded - skipping");
            return;
        }

        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var authorities = new List<Authority>
        {
            // === PMB (city-wide) ===
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000000001"),
                Name = "Primăria Municipiului București",
                Email = "relatiipublice@pmb.ro",
                County = "București",
                City = "București",
                District = null,
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000000002"),
                Name = "Poliția Locală a Municipiului București",
                Email = "office@plmb.ro",
                County = "București",
                City = "București",
                District = null,
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000000003"),
                Name = "ASPA București",
                Email = "relatiicupublicul@aspa.ro",
                County = "București",
                City = "București",
                District = null,
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000000004"),
                Name = "Protecția Animalelor CJ Ilfov",
                Email = "protectiaanimalelor@cjilfov.ro",
                County = "București",
                City = "București",
                District = null,
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000000005"),
                Name = "Administrația Străzilor București",
                Email = "office@aspmb.ro",
                County = "București",
                City = "București",
                District = null,
                IsActive = true,
                CreatedAt = seedDate
            },

            // === Sector 1 ===
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000001001"),
                Name = "Primăria Sector 1",
                Email = "registratura@primarias1.ro",
                County = "București",
                City = "București",
                District = "Sector 1",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000001002"),
                Name = "DGASPC Sector 1",
                Email = "registratura@dgaspc-sectorul1.ro",
                County = "București",
                City = "București",
                District = "Sector 1",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000001003"),
                Name = "Poliția Locală S1",
                Email = "contact@politialocalasector1.ro",
                County = "București",
                City = "București",
                District = "Sector 1",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000001004"),
                Name = "Poliția Locală S1 (Protecția Animalelor)",
                Email = "politiaanimalelor@politialocalasector1.ro",
                County = "București",
                City = "București",
                District = "Sector 1",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000001005"),
                Name = "Protecția Animalelor S1",
                Email = "protectiaanimalelor@primarias1.ro",
                County = "București",
                City = "București",
                District = "Sector 1",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000001006"),
                Name = "Direcția de Utilități Publice, Salubrizare și Protecția Mediului S1",
                Email = "secretariat@mediusectorul1.ro",
                County = "București",
                City = "București",
                District = "Sector 1",
                IsActive = true,
                CreatedAt = seedDate
            },

            // === Sector 2 ===
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000002001"),
                Name = "Primăria Sector 2",
                Email = "infopublice@ps2.ro",
                County = "București",
                City = "București",
                District = "Sector 2",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000002002"),
                Name = "Poliția Locală S2",
                Email = "directorgeneral.dgpl@politialocalas2.ro",
                County = "București",
                City = "București",
                District = "Sector 2",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000002003"),
                Name = "Asistenţă Socială si Protecţia Copilului S2",
                Email = "social@social2.ro",
                County = "București",
                City = "București",
                District = "Sector 2",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000002004"),
                Name = "Primar Sector 2",
                Email = "primar@ps2.ro",
                County = "București",
                City = "București",
                District = "Sector 2",
                IsActive = true,
                CreatedAt = seedDate
            },

            // === Sector 3 ===
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003001"),
                Name = "Primăria Sector 3",
                Email = "relatiipublice@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003002"),
                Name = "Poliția Locală S3",
                Email = "secretariat.dgpl@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003003"),
                Name = "Salubritate S3",
                Email = "comunicare@salubritate3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003004"),
                Name = "Directia Parcari",
                Email = "directiaparcari@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003005"),
                Name = "Ordine Publică și Control",
                Email = "ordinepublica.control@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003006"),
                Name = "Serviciul Circulație pe Dumurile Publice",
                Email = "circulatie@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003007"),
                Name = "Serviciul Disciplina în Construcții",
                Email = "disciplinainconstructii@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000003008"),
                Name = "Serviciul Control Protecția Mediului",
                Email = "mediu@primarie3.ro",
                County = "București",
                City = "București",
                District = "Sector 3",
                IsActive = true,
                CreatedAt = seedDate
            },

            // === Sector 4 ===
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000004001"),
                Name = "Primăria Sector 4",
                Email = "contact@ps4.ro",
                County = "București",
                City = "București",
                District = "Sector 4",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000004002"),
                Name = "Poliția Locală S4",
                Email = "sesizari@politialocala4.ro",
                County = "București",
                City = "București",
                District = "Sector 4",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000004003"),
                Name = "DGASPC Sector 4",
                Email = "contact@dgaspc4.ro",
                County = "București",
                City = "București",
                District = "Sector 4",
                IsActive = true,
                CreatedAt = seedDate
            },

            // === Sector 5 ===
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000005001"),
                Name = "Primăria Sector 5",
                Email = "primarie@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000005002"),
                Name = "Serviciul de Ordine Publică și Intervenție Rapidă",
                Email = "ordinepublica@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000005003"),
                Name = "Serviciul Politia Animalelor",
                Email = "politiaanimalelor@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000005004"),
                Name = "Serviciul Monitorizare Și Control Salubrizare",
                Email = "salubrizare@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000005005"),
                Name = "Serviciul Spații Verzi",
                Email = "spatiiverzi@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000005006"),
                Name = "Administrația Dezvoltare Urbană",
                Email = "dezvoltareurbana@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000005007"),
                Name = "Poliția Locală S5",
                Email = "politialocala@sector5.ro",
                County = "București",
                City = "București",
                District = "Sector 5",
                IsActive = true,
                CreatedAt = seedDate
            },

            // === Sector 6 ===
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000006001"),
                Name = "Primăria Sector 6",
                Email = "prim6@primarie6.ro",
                County = "București",
                City = "București",
                District = "Sector 6",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000006002"),
                Name = "Poliţia Locală S6",
                Email = "contact@politia6.ro",
                County = "București",
                City = "București",
                District = "Sector 6",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000006003"),
                Name = "DGASPC Sector 6",
                Email = "office@dgaspc6.com",
                County = "București",
                City = "București",
                District = "Sector 6",
                IsActive = true,
                CreatedAt = seedDate
            },
            new()
            {
                Id = Guid.Parse("a0000000-0000-0000-0000-000000006004"),
                Name = "ADP Sector 6",
                Email = "contact@adps6.ro",
                County = "București",
                City = "București",
                District = "Sector 6",
                IsActive = true,
                CreatedAt = seedDate
            }
        };

        context.Authorities.AddRange(authorities);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} authorities", authorities.Count);
    }
}
