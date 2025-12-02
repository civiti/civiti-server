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

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded 1 demo user and {IssueCount} demo issues", demoIssues.Count);
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
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
                Status = IssueStatus.Approved,
                EmailsSent = 156,
                PublicVisibility = true,
                Priority = Priority.Critical,
                CreatedAt = now.AddDays(-30),
                UpdatedAt = now
            }
        ];
    }
}
