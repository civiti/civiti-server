using Civica.Api.Data;
using Civica.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Civica.Api.Services;

/// <summary>
/// Seeds demo data for development environments.
/// Only registered in Development environment (see Program.cs).
/// Checks if demo data already exists before seeding (idempotent).
/// </summary>
public class DemoDataSeeder : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DemoDataSeeder> _logger;

    // Fixed GUIDs for demo data (allows idempotent seeding)
    private static readonly Guid DemoUserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    // Authority GUIDs from migration (must match InitialCreate.cs)
    private static readonly Guid AuthorityPMB = Guid.Parse("a0000001-0000-0000-0000-000000000001");      // Primăria București
    private static readonly Guid AuthoritySector1 = Guid.Parse("a0000001-0000-0000-0000-000000000002"); // Sector 1
    private static readonly Guid AuthoritySector2 = Guid.Parse("a0000001-0000-0000-0000-000000000003"); // Sector 2
    private static readonly Guid AuthoritySector3 = Guid.Parse("a0000001-0000-0000-0000-000000000004"); // Sector 3
    private static readonly Guid AuthoritySector4 = Guid.Parse("a0000001-0000-0000-0000-000000000005"); // Sector 4
    private static readonly Guid AuthoritySector5 = Guid.Parse("a0000001-0000-0000-0000-000000000006"); // Sector 5
    private static readonly Guid AuthoritySector6 = Guid.Parse("a0000001-0000-0000-0000-000000000007"); // Sector 6

    public DemoDataSeeder(
        IServiceProvider serviceProvider,
        ILogger<DemoDataSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting demo data seeding");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();

            // Check if demo user already exists
            if (await context.UserProfiles.AnyAsync(u => u.Id == DemoUserId, cancellationToken))
            {
                _logger.LogInformation("Demo data already exists - skipping seeding");
                return;
            }

            await SeedDemoDataAsync(context, cancellationToken);
            _logger.LogInformation("Demo data seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding demo data");
            // Don't throw - we don't want to prevent app startup
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedDemoDataAsync(CivicaDbContext context, CancellationToken cancellationToken)
    {
        // Create demo user
        var demoUser = CreateDemoUser();
        context.UserProfiles.Add(demoUser);

        // Create demo issues
        var demoIssues = CreateDemoIssues();
        context.Issues.AddRange(demoIssues);

        // Create demo issue-authority links
        var demoIssueAuthorities = CreateDemoIssueAuthorities();
        context.IssueAuthorities.AddRange(demoIssueAuthorities);

        // Create demo photos for issues
        var demoPhotos = CreateDemoPhotos();
        context.IssuePhotos.AddRange(demoPhotos);

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded 1 demo user, {IssueCount} demo issues, {LinkCount} issue-authority links, and {PhotoCount} photos",
            demoIssues.Count, demoIssueAuthorities.Count, demoPhotos.Count);
    }

    private static UserProfile CreateDemoUser()
    {
        return new UserProfile
        {
            Id = DemoUserId,
            SupabaseUserId = "demo-user-001",
            Email = "demo@civica.ro",
            DisplayName = "Maria Ionescu",
            PhotoUrl = null,
            County = "București",
            City = "București",
            District = "Sector 3",
            Points = 150,
            Level = 2,
            IssuesReported = 10,
            IssuesResolved = 1,
            CommunityVotes = 10,
            CommentsGiven = 3,
            HelpfulComments = 2,
            QualityScore = 85.00m,
            ApprovalRate = 90.00m,
            CurrentLoginStreak = 3,
            LongestLoginStreak = 7,
            CurrentVotingStreak = 2,
            LongestVotingStreak = 5,
            LastActivityDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IssueUpdatesEnabled = true,
            CommunityNewsEnabled = true,
            MonthlyDigestEnabled = false,
            AchievementsEnabled = true,
            EmailVerified = true
        };
    }

    private static List<Issue> CreateDemoIssues()
    {
        var now = DateTime.UtcNow;

        return
        [
            // Issue 1: Infrastructure - Gropi pe Lipscani
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0001-0001-0001-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Gropi periculoase pe Strada Lipscani",
                Description = "Pe Strada Lipscani, între intersecția cu Strada Smârdan și Banca Națională, există multiple gropi adânci în asfalt care prezintă pericol pentru pietoni și șoferi. Gropile au apărut în urma lucrărilor de canalizare din vara trecută și nu au fost reparate corespunzător.",
                Category = IssueCategory.Infrastructure,
                Address = "Strada Lipscani 45, București",
                Latitude = 44.4323,
                Longitude = 26.0986,
                LocationAccuracy = 10,
                Neighborhood = "Centrul Vechi",
                District = "Sector 3",
                Urgency = UrgencyLevel.High,
                Status = IssueStatus.Active,
                EmailsSent = 23,
                PublicVisibility = true,
                Priority = Priority.High,
                CreatedAt = now.AddDays(-5),
                UpdatedAt = now
            },

            // Issue 2: Environment - Gunoi în Herăstrău
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0002-0002-0002-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Gunoi abandonat în Parcul Herăstrău",
                Description = "În zona de nord a Parcului Herăstrău, lângă lacul principal, se acumulează constant gunoaie abandonate. Coșurile de gunoi sunt insuficiente și rareori golite. Situația afectează fauna locală și aspectul parcului.",
                Category = IssueCategory.Environment,
                Address = "Parcul Herăstrău, Aleea Privighetorilor, București",
                Latitude = 44.4747,
                Longitude = 26.0819,
                LocationAccuracy = 15,
                Neighborhood = "Herăstrău",
                District = "Sector 1",
                Urgency = UrgencyLevel.Medium,
                Status = IssueStatus.Active,
                EmailsSent = 45,
                PublicVisibility = true,
                Priority = Priority.Medium,
                CreatedAt = now.AddDays(-3),
                UpdatedAt = now
            },

            // Issue 3: Transportation - Stație STB
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0003-0003-0003-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Stație STB fără acoperiș - Piața Romană",
                Description = "Stația de autobuz din Piața Romană (direcția Universitate) nu are acoperiș de peste 6 luni. Călătorii sunt expuși intemperiilor în timp ce așteaptă transportul public.",
                Category = IssueCategory.Transportation,
                Address = "Piața Romană, București",
                Latitude = 44.4486,
                Longitude = 26.0967,
                LocationAccuracy = 8,
                Neighborhood = "Piața Romană",
                District = "Sector 1",
                Urgency = UrgencyLevel.Medium,
                Status = IssueStatus.Active,
                EmailsSent = 67,
                PublicVisibility = true,
                Priority = Priority.Medium,
                CreatedAt = now.AddDays(-7),
                UpdatedAt = now
            },

            // Issue 4: Safety - Iluminat defect
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0004-0004-0004-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Iluminat stradal defect pe Calea Victoriei",
                Description = "Pe Calea Victoriei, între Piața Revoluției și Hotel Intercontinental, aproximativ 12 stâlpi de iluminat nu funcționează. Zona devine periculoasă după lăsarea întunericului.",
                Category = IssueCategory.Safety,
                Address = "Calea Victoriei 155, București",
                Latitude = 44.4410,
                Longitude = 26.0970,
                LocationAccuracy = 5,
                Neighborhood = "Centru",
                District = "Sector 1",
                Urgency = UrgencyLevel.Urgent,
                Status = IssueStatus.Active,
                EmailsSent = 89,
                PublicVisibility = true,
                Priority = Priority.Critical,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now
            },

            // Issue 5: PublicServices - Hidrant
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0005-0005-0005-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Hidrant nefuncțional pe Bulevardul Unirii",
                Description = "Hidrantul de incendiu de la intersecția Bd. Unirii cu Str. Nerva Traian este deteriorat și nefuncțional. În caz de incendiu, pompierii nu ar avea acces la apă în această zonă.",
                Category = IssueCategory.PublicServices,
                Address = "Bulevardul Unirii 64, București",
                Latitude = 44.4244,
                Longitude = 26.1114,
                LocationAccuracy = 10,
                Neighborhood = "Unirii",
                District = "Sector 3",
                Urgency = UrgencyLevel.High,
                Status = IssueStatus.Active,
                EmailsSent = 34,
                PublicVisibility = true,
                Priority = Priority.High,
                CreatedAt = now.AddDays(-10),
                UpdatedAt = now
            },

            // Issue 6: Infrastructure - Trotuar degradat
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0006-0006-0006-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Trotuar degradat pe Șoseaua Panduri",
                Description = "Trotuarul de pe Șoseaua Panduri, în dreptul Facultății de Drept, este complet degradat. Plăcile de beton sunt sparte și ridicate, reprezentând un pericol pentru pietoni, în special pentru persoanele cu dizabilități.",
                Category = IssueCategory.Infrastructure,
                Address = "Șoseaua Panduri 90, București",
                Latitude = 44.4350,
                Longitude = 26.0720,
                LocationAccuracy = 12,
                Neighborhood = "Cotroceni",
                District = "Sector 5",
                Urgency = UrgencyLevel.Medium,
                Status = IssueStatus.Active,
                EmailsSent = 28,
                PublicVisibility = true,
                Priority = Priority.Medium,
                CreatedAt = now.AddDays(-14),
                UpdatedAt = now
            },

            // Issue 7: Transportation - Semafoare (Submitted - pending review)
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0007-0007-0007-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Semafoare nesincronizate la Obor",
                Description = "Semafoarele de la intersecția Șoseaua Colentina cu Șoseaua Mihai Bravu sunt complet nesincronizate, creând blocaje în trafic și situații periculoase.",
                Category = IssueCategory.Transportation,
                Address = "Piața Obor, București",
                Latitude = 44.4528,
                Longitude = 26.1264,
                LocationAccuracy = 20,
                Neighborhood = "Obor",
                District = "Sector 2",
                Urgency = UrgencyLevel.High,
                Status = IssueStatus.Submitted,
                EmailsSent = 0,
                PublicVisibility = false,
                Priority = Priority.Medium,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            },

            // Issue 8: Environment - Copaci uscați
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0008-0008-0008-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Copaci uscați periculoși în Parcul Cișmigiu",
                Description = "În Parcul Cișmigiu există cel puțin 5 copaci uscați care prezintă risc de cădere. Crengile mari pot cădea oricând peste vizitatori sau bănci.",
                Category = IssueCategory.Environment,
                Address = "Parcul Cișmigiu, București",
                Latitude = 44.4370,
                Longitude = 26.0890,
                LocationAccuracy = 25,
                Neighborhood = "Cișmigiu",
                District = "Sector 1",
                Urgency = UrgencyLevel.High,
                Status = IssueStatus.Active,
                EmailsSent = 52,
                PublicVisibility = true,
                Priority = Priority.High,
                CreatedAt = now.AddDays(-6),
                UpdatedAt = now
            },

            // Issue 9: Other - Rampe dizabilități
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0009-0009-0009-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Lipsă rampe pentru persoane cu dizabilități - Primăria Sector 4",
                Description = "Clădirea Primăriei Sectorului 4 nu are rampe de acces pentru persoane cu dizabilități sau mame cu cărucioare. Accesul se face doar pe scări.",
                Category = IssueCategory.Other,
                Address = "Bulevardul Metalurgiei 12-18, București",
                Latitude = 44.3900,
                Longitude = 26.1180,
                LocationAccuracy = 15,
                Neighborhood = "Berceni",
                District = "Sector 4",
                Urgency = UrgencyLevel.Medium,
                Status = IssueStatus.Active,
                EmailsSent = 41,
                PublicVisibility = true,
                Priority = Priority.Medium,
                CreatedAt = now.AddDays(-20),
                UpdatedAt = now
            },

            // Issue 10: Infrastructure - Canalizare (high engagement)
            new Issue
            {
                Id = Guid.Parse("eeeeeeee-0010-0010-0010-eeeeeeeeeeee"),
                UserId = DemoUserId,
                Title = "Canalizare înfundată pe Strada Știrbei Vodă",
                Description = "Canalizarea de pe Strada Știrbei Vodă este înfundată de luni de zile. La fiecare ploaie, strada se inundă complet, blocând accesul pietonilor și vehiculelor. Mirosul este insuportabil.",
                Category = IssueCategory.Infrastructure,
                Address = "Strada Știrbei Vodă 104, București",
                Latitude = 44.4420,
                Longitude = 26.0830,
                LocationAccuracy = 8,
                Neighborhood = "Cișmigiu",
                District = "Sector 1",
                Urgency = UrgencyLevel.Urgent,
                Status = IssueStatus.Active,
                EmailsSent = 156,
                PublicVisibility = true,
                Priority = Priority.Critical,
                CreatedAt = now.AddDays(-30),
                UpdatedAt = now
            }
        ];
    }

    /// <summary>
    /// Creates issue-authority links for demo data.
    /// Links each issue to the appropriate sector authority based on the issue's district.
    /// </summary>
    private static List<IssueAuthority> CreateDemoIssueAuthorities()
    {
        return
        [
            // Issue 1 (Gropi Lipscani - Sector 3) -> Primăria Sector 3
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0001-0001-0001-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0001-0001-0001-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector3,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 2 (Gunoi Herăstrău - Sector 1) -> Primăria Sector 1
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0002-0001-0002-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0002-0002-0002-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector1,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 3 (Stație STB - Sector 1) -> Primăria Sector 1
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0003-0001-0003-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0003-0003-0003-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector1,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 4 (Iluminat Calea Victoriei - Sector 1) -> Primăria București + Sector 1
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0004-0001-0004-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0004-0004-0004-eeeeeeeeeeee"),
                AuthorityId = AuthorityPMB,
                CreatedAt = DateTime.UtcNow
            },
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0004-0002-0004-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0004-0004-0004-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector1,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 5 (Hidrant - Sector 3) -> Primăria Sector 3
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0005-0001-0005-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0005-0005-0005-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector3,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 6 (Trotuar Panduri - Sector 5) -> Primăria Sector 5
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0006-0001-0006-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0006-0006-0006-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector5,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 7 (Semafoare Obor - Sector 2) -> Primăria București + Sector 2
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0007-0001-0007-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0007-0007-0007-eeeeeeeeeeee"),
                AuthorityId = AuthorityPMB,
                CreatedAt = DateTime.UtcNow
            },
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0007-0002-0007-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0007-0007-0007-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector2,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 8 (Copaci Cișmigiu - Sector 1) -> Primăria Sector 1
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0008-0001-0008-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0008-0008-0008-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector1,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 9 (Rampe Sector 4) -> Primăria Sector 4
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0009-0001-0009-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0009-0009-0009-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector4,
                CreatedAt = DateTime.UtcNow
            },

            // Issue 10 (Canalizare Știrbei Vodă - Sector 1) -> Primăria București + Sector 1
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0010-0001-0010-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0010-0010-0010-eeeeeeeeeeee"),
                AuthorityId = AuthorityPMB,
                CreatedAt = DateTime.UtcNow
            },
            new IssueAuthority
            {
                Id = Guid.Parse("bbbbbbbb-0010-0002-0010-bbbbbbbbbbbb"),
                IssueId = Guid.Parse("eeeeeeee-0010-0010-0010-eeeeeeeeeeee"),
                AuthorityId = AuthoritySector1,
                CreatedAt = DateTime.UtcNow
            }
        ];
    }

    /// <summary>
    /// Creates demo photos for each issue.
    /// Uses placeholder images - replace URLs with real images as needed.
    /// </summary>
    private static List<IssuePhoto> CreateDemoPhotos()
    {
        var photos = new List<IssuePhoto>();
        var issueIds = new[]
        {
            "eeeeeeee-0001-0001-0001-eeeeeeeeeeee", // Gropi Lipscani
            "eeeeeeee-0002-0002-0002-eeeeeeeeeeee", // Gunoi Herăstrău
            "eeeeeeee-0003-0003-0003-eeeeeeeeeeee", // Stație STB
            "eeeeeeee-0004-0004-0004-eeeeeeeeeeee", // Iluminat
            "eeeeeeee-0005-0005-0005-eeeeeeeeeeee", // Hidrant
            "eeeeeeee-0006-0006-0006-eeeeeeeeeeee", // Trotuar
            "eeeeeeee-0007-0007-0007-eeeeeeeeeeee", // Semafoare
            "eeeeeeee-0008-0008-0008-eeeeeeeeeeee", // Copaci
            "eeeeeeee-0009-0009-0009-eeeeeeeeeeee", // Rampe
            "eeeeeeee-0010-0010-0010-eeeeeeeeeeee"  // Canalizare
        };

        var photoDescriptions = new[]
        {
            ("Vedere de ansamblu a problemei", "Detaliu apropiat"),
            ("Situația actuală în parc", "Zona afectată"),
            ("Stația fără acoperiș", "Vedere laterală"),
            ("Stâlpi nefuncționali", "Zona întunecată noaptea"),
            ("Hidrantul deteriorat", "Detaliu damage"),
            ("Trotuarul degradat", "Plăci sparte"),
            ("Intersecția problematică", "Vedere din trafic"),
            ("Copaci uscați", "Crengi periculoase"),
            ("Intrarea fără rampă", "Scările de acces"),
            ("Strada inundată", "Canalizarea înfundată")
        };

        for (int i = 0; i < issueIds.Length; i++)
        {
            var issueId = Guid.Parse(issueIds[i]);
            var (primaryDesc, secondaryDesc) = photoDescriptions[i];

            // Primary photo
            photos.Add(new IssuePhoto
            {
                Id = Guid.Parse($"cccccccc-{i + 1:D4}-0001-0001-cccccccccccc"),
                IssueId = issueId,
                Url = $"https://picsum.photos/seed/civica{i + 1}a/800/600",
                ThumbnailUrl = $"https://picsum.photos/seed/civica{i + 1}a/200/150",
                Caption = primaryDesc,
                Description = $"Fotografie principală - {primaryDesc}",
                IsPrimary = true,
                Quality = PhotoQuality.High,
                FileSize = 245000,
                Width = 800,
                Height = 600,
                Format = "jpg",
                CreatedAt = DateTime.UtcNow.AddDays(-i - 1)
            });

            // Secondary photo
            photos.Add(new IssuePhoto
            {
                Id = Guid.Parse($"cccccccc-{i + 1:D4}-0002-0002-cccccccccccc"),
                IssueId = issueId,
                Url = $"https://picsum.photos/seed/civica{i + 1}b/800/600",
                ThumbnailUrl = $"https://picsum.photos/seed/civica{i + 1}b/200/150",
                Caption = secondaryDesc,
                Description = $"Fotografie secundară - {secondaryDesc}",
                IsPrimary = false,
                Quality = PhotoQuality.High,
                FileSize = 198000,
                Width = 800,
                Height = 600,
                Format = "jpg",
                CreatedAt = DateTime.UtcNow.AddDays(-i - 1)
            });
        }

        return photos;
    }
}
