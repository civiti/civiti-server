# Civica Backend Implementation Guide

## 🎯 Overview

This comprehensive guide provides the **csharp-expert agent** with everything needed to build a .NET 9 Minimal API backend that seamlessly replaces the existing Angular mock services. The frontend is feature-complete and ready for integration.

### Architecture Summary
```
Angular Frontend (Ready) ←→ .NET 9 Minimal API ←→ PostgreSQL + Supabase Auth
```

## 📋 Prerequisites

- .NET 9 SDK
- PostgreSQL 16+ (Railway managed)
- Supabase project configured
- Railway account for deployment
- Visual Studio 2022/VS Code with C# extensions

## 🏗️ Project Structure

Create the following project structure:

```
Civica.Backend/
├── Civica.Api/                    # Main API project
│   ├── Program.cs                 # Entry point and configuration
│   ├── Endpoints/                 # Minimal API endpoint definitions
│   │   ├── AuthEndpoints.cs
│   │   ├── IssueEndpoints.cs
│   │   ├── UserEndpoints.cs
│   │   ├── AdminEndpoints.cs
│   │   └── GamificationEndpoints.cs
│   ├── Services/                  # Business logic services
│   │   ├── Interfaces/
│   │   │   ├── IAuthService.cs
│   │   │   ├── IIssueService.cs
│   │   │   ├── IUserService.cs
│   │   │   ├── IAdminService.cs
│   │   │   ├── IGamificationService.cs
│   │   │   └── ISupabaseService.cs
│   │   ├── AuthService.cs
│   │   ├── IssueService.cs
│   │   ├── UserService.cs
│   │   ├── AdminService.cs
│   │   ├── GamificationService.cs
│   │   └── SupabaseService.cs
│   ├── Models/                    # Domain models and DTOs
│   │   ├── Domain/
│   │   │   ├── User.cs
│   │   │   ├── Issue.cs
│   │   │   ├── IssuePhoto.cs
│   │   │   ├── Badge.cs
│   │   │   ├── Achievement.cs
│   │   │   └── AdminAction.cs
│   │   ├── Requests/
│   │   │   ├── Auth/
│   │   │   ├── Issues/
│   │   │   ├── User/
│   │   │   └── Admin/
│   │   └── Responses/
│   │       ├── Auth/
│   │       ├── Issues/
│   │       ├── User/
│   │       └── Admin/
│   ├── Data/                      # EF Core context and configurations
│   │   ├── CivicaDbContext.cs
│   │   ├── Configurations/
│   │   │   ├── UserConfiguration.cs
│   │   │   ├── IssueConfiguration.cs
│   │   │   ├── IssuePhotoConfiguration.cs
│   │   │   └── BadgeConfiguration.cs
│   │   └── Migrations/
│   ├── Infrastructure/            # Cross-cutting concerns
│   │   ├── Middleware/
│   │   │   ├── ErrorHandlingMiddleware.cs
│   │   │   └── RequestLoggingMiddleware.cs
│   │   ├── Extensions/
│   │   │   ├── ServiceCollectionExtensions.cs
│   │   │   └── WebApplicationExtensions.cs
│   │   ├── Validators/
│   │   │   ├── AuthValidators.cs
│   │   │   ├── IssueValidators.cs
│   │   │   └── UserValidators.cs
│   │   └── Constants/
│   │       ├── ApiRoutes.cs
│   │       └── ErrorMessages.cs
│   └── appsettings.json
└── Civica.Tests/                  # Test project (optional but recommended)
    ├── Unit/
    ├── Integration/
    └── Civica.Tests.csproj
```

## 📦 Package Dependencies

Add these packages to `Civica.Api.csproj`:

```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.0" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.0.0" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="7.1.2" />
<PackageReference Include="FluentValidation.AspNetCore" Version="11.3.0" />
<PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
<PackageReference Include="supabase-csharp" Version="0.13.3" />
<PackageReference Include="Postgrest" Version="3.4.0" />
```

## ⚙️ Configuration Setup

### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=civica_dev;Username=postgres;Password=password"
  },
  "Supabase": {
    "Url": "https://your-project.supabase.co",
    "AnonKey": "your-anon-key",
    "ServiceRoleKey": "your-service-role-key"
  },
  "JwtSettings": {
    "ValidIssuer": "https://your-project.supabase.co",
    "ValidAudience": "authenticated",
    "RequireHttpsMetadata": true,
    "ValidateIssuerSigningKey": true
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:4200",
      "https://your-frontend-domain.com"
    ]
  },
  "Railway": {
    "DatabaseUrl": "$DATABASE_URL",
    "Port": "$PORT"
  }
}
```

### Environment Variables (Railway)
```bash
DATABASE_URL=postgresql://user:pass@host:port/database
SUPABASE_URL=https://xxx.supabase.co
SUPABASE_ANON_KEY=xxx
SUPABASE_SERVICE_KEY=xxx
PORT=8080
ASPNETCORE_ENVIRONMENT=Production
```

## 🔧 Implementation Steps

### Step 1: Core Infrastructure Setup

#### Program.cs
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using FluentValidation;
using Civica.Api.Data;
using Civica.Api.Services.Interfaces;
using Civica.Api.Services;
using Civica.Api.Infrastructure.Extensions;
using Civica.Api.Infrastructure.Middleware;
using Civica.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/civica-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") 
    ?? builder.Configuration.GetConnectionString("PostgreSQL");

if (connectionString?.StartsWith("postgres://") == true)
{
    // Convert Railway DATABASE_URL format
    connectionString = connectionString.Replace("postgres://", "");
    var parts = connectionString.Split('@');
    var userInfo = parts[0].Split(':');
    var hostInfo = parts[1].Split('/');
    var hostPortInfo = hostInfo[0].Split(':');
    
    connectionString = $"Host={hostPortInfo[0]};Port={hostPortInfo[1]};Database={hostInfo[1]};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
}

builder.Services.AddDbContext<CivicaDbContext>(options =>
    options.UseNpgsql(connectionString));

// Authentication with Supabase JWT
var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") 
    ?? builder.Configuration["Supabase:Url"];
var supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") 
    ?? builder.Configuration["Supabase:AnonKey"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = supabaseUrl;
        options.Audience = "authenticated";
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidIssuer = supabaseUrl,
            ValidAudience = "authenticated"
        };
    });

builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("CivicaPolicy", policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:4200" })
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Custom services
builder.Services.AddScoped<ISupabaseService, SupabaseService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IIssueService, IssueService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IGamificationService, GamificationService>();

// Validators
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("CivicaPolicy");
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapIssueEndpoints();
app.MapAdminEndpoints();
app.MapGamificationEndpoints();

// Health check
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Database migration on startup (Railway compatible)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();
    try
    {
        await context.Database.MigrateAsync();
        Log.Information("Database migration completed successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Database migration failed");
        throw;
    }
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
```

### Step 2: Database Context and Models

#### CivicaDbContext.cs
```csharp
using Microsoft.EntityFrameworkCore;
using Civica.Api.Models.Domain;
using Civica.Api.Data.Configurations;

namespace Civica.Api.Data;

public class CivicaDbContext : DbContext
{
    public CivicaDbContext(DbContextOptions<CivicaDbContext> options) : base(options) { }

    public DbSet<UserProfile> UserProfiles { get; set; } = null!;
    public DbSet<Issue> Issues { get; set; } = null!;
    public DbSet<IssuePhoto> IssuePhotos { get; set; } = null!;
    public DbSet<Badge> Badges { get; set; } = null!;
    public DbSet<Achievement> Achievements { get; set; } = null!;
    public DbSet<UserBadge> UserBadges { get; set; } = null!;
    public DbSet<UserAchievement> UserAchievements { get; set; } = null!;
    public DbSet<AdminAction> AdminActions { get; set; } = null!;
    public DbSet<EmailTracking> EmailTrackings { get; set; } = null!;

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
        modelBuilder.ApplyConfiguration(new EmailTrackingConfiguration());

        // Seed data
        SeedBadges(modelBuilder);
        SeedAchievements(modelBuilder);
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
                CreatedAt = DateTime.UtcNow
            }
            // Add more badges...
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
                CreatedAt = DateTime.UtcNow
            }
            // Add more achievements...
        );
    }
}
```

### Step 3: Domain Models

#### Models/Domain/User.cs
```csharp
namespace Civica.Api.Models.Domain;

public class UserProfile
{
    public Guid Id { get; set; }
    public string SupabaseUserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string County { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public ResidenceType? ResidenceType { get; set; }
    public bool IssueUpdatesEnabled { get; set; } = true;
    public bool CommunityNewsEnabled { get; set; } = true;
    public bool MonthlyDigestEnabled { get; set; } = false;
    public bool AchievementsEnabled { get; set; } = true;
    public int Points { get; set; } = 0;
    public int Level { get; set; } = 1;
    public int IssuesReported { get; set; } = 0;
    public int IssuesResolved { get; set; } = 0;
    public int CommunityVotes { get; set; } = 0;
    public int CommentsGiven { get; set; } = 0;
    public int HelpfulComments { get; set; } = 0;
    public decimal QualityScore { get; set; } = 0;
    public decimal ApprovalRate { get; set; } = 0;
    public int CurrentLoginStreak { get; set; } = 0;
    public int LongestLoginStreak { get; set; } = 0;
    public int CurrentVotingStreak { get; set; } = 0;
    public int LongestVotingStreak { get; set; } = 0;
    public DateTime LastActivityDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool EmailVerified { get; set; } = false;

    // Navigation properties
    public List<Issue> Issues { get; set; } = new();
    public List<UserBadge> UserBadges { get; set; } = new();
    public List<UserAchievement> UserAchievements { get; set; } = new();
}

public enum ResidenceType
{
    Apartment,
    House,
    Business
}
```

#### Models/Domain/Issue.cs
```csharp
namespace Civica.Api.Models.Domain;

public class Issue
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int LocationAccuracy { get; set; }
    public string? Neighborhood { get; set; }
    public string? Landmark { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.Submitted;
    public int EmailsSent { get; set; } = 0;
    public string? CurrentSituation { get; set; }
    public string? DesiredOutcome { get; set; }
    public string? CommunityImpact { get; set; }
    public string? AIGeneratedDescription { get; set; }
    public string? AIProposedSolution { get; set; }
    public decimal? AIConfidence { get; set; }
    public string? AdminNotes { get; set; }
    public string? RejectionReason { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public string? AssignedDepartment { get; set; }
    public string? EstimatedResolutionTime { get; set; }
    public bool PublicVisibility { get; set; } = true;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public UserProfile User { get; set; } = null!;
    public List<IssuePhoto> Photos { get; set; } = new();
    public List<AdminAction> AdminActions { get; set; } = new();
    public List<EmailTracking> EmailTrackings { get; set; } = new();
}

public enum IssueCategory
{
    Infrastructure,
    Environment,
    Transportation,
    PublicServices,
    Safety,
    Other
}

public enum UrgencyLevel
{
    Low,
    Medium,
    High,
    Urgent
}

public enum IssueStatus
{
    Draft,
    Submitted,
    UnderReview,
    Approved,
    InProgress,
    Resolved,
    Rejected
}

public enum Priority
{
    Low,
    Medium,
    High,
    Critical
}
```

### Step 4: Service Implementations

#### Services/IssueService.cs
```csharp
using Microsoft.EntityFrameworkCore;
using Civica.Api.Data;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Models.Responses.Issues;
using Civica.Api.Services.Interfaces;

namespace Civica.Api.Services;

public class IssueService : IIssueService
{
    private readonly CivicaDbContext _context;
    private readonly IGamificationService _gamificationService;
    private readonly ILogger<IssueService> _logger;

    public IssueService(CivicaDbContext context, IGamificationService gamificationService, ILogger<IssueService> logger)
    {
        _context = context;
        _gamificationService = gamificationService;
        _logger = logger;
    }

    public async Task<PagedResult<IssueListResponse>> GetAllIssuesAsync(GetIssuesRequest request)
    {
        var query = _context.Issues
            .Include(i => i.Photos)
            .Include(i => i.User)
            .Where(i => i.Status == IssueStatus.Approved && i.PublicVisibility)
            .AsQueryable();

        // Apply filters
        if (request.Category.HasValue)
        {
            query = query.Where(i => i.Category == request.Category.Value);
        }

        if (request.Urgency.HasValue)
        {
            query = query.Where(i => i.Urgency == request.Urgency.Value);
        }

        if (!string.IsNullOrEmpty(request.District))
        {
            query = query.Where(i => i.User.District == request.District);
        }

        // Apply sorting
        query = request.SortBy switch
        {
            "emails" => request.SortDescending ? 
                query.OrderByDescending(i => i.EmailsSent) : 
                query.OrderBy(i => i.EmailsSent),
            "urgency" => request.SortDescending ? 
                query.OrderByDescending(i => i.Urgency) : 
                query.OrderBy(i => i.Urgency),
            _ => request.SortDescending ? 
                query.OrderByDescending(i => i.CreatedAt) : 
                query.OrderBy(i => i.CreatedAt)
        };

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(i => new IssueListResponse
            {
                Id = i.Id,
                Title = i.Title,
                Description = i.Description.Length > 200 ? 
                    i.Description.Substring(0, 200) + "..." : 
                    i.Description,
                Category = i.Category,
                Address = i.Address,
                Urgency = i.Urgency,
                EmailsSent = i.EmailsSent,
                CreatedAt = i.CreatedAt,
                MainPhotoUrl = i.Photos.OrderBy(p => p.CreatedAt).FirstOrDefault() != null ? 
                    i.Photos.OrderBy(p => p.CreatedAt).First().Url : null
            })
            .ToListAsync();

        return new PagedResult<IssueListResponse>
        {
            Items = items,
            TotalItems = totalItems,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling((double)totalItems / request.PageSize)
        };
    }

    public async Task<IssueDetailResponse?> GetIssueByIdAsync(Guid id)
    {
        var issue = await _context.Issues
            .Include(i => i.Photos)
            .Include(i => i.User)
            .FirstOrDefaultAsync(i => i.Id == id && i.Status == IssueStatus.Approved && i.PublicVisibility);

        if (issue == null)
            return null;

        return new IssueDetailResponse
        {
            Id = issue.Id,
            Title = issue.Title,
            Description = issue.Description,
            Category = issue.Category,
            Address = issue.Address,
            Latitude = issue.Latitude,
            Longitude = issue.Longitude,
            Urgency = issue.Urgency,
            EmailsSent = issue.EmailsSent,
            CurrentSituation = issue.CurrentSituation,
            DesiredOutcome = issue.DesiredOutcome,
            CommunityImpact = issue.CommunityImpact,
            CreatedAt = issue.CreatedAt,
            Photos = issue.Photos.Select(p => new IssuePhotoResponse
            {
                Id = p.Id,
                Url = p.Url,
                ThumbnailUrl = p.ThumbnailUrl,
                Caption = p.Caption
            }).ToList(),
            User = new IssueUserResponse
            {
                Id = issue.User.Id,
                DisplayName = issue.User.DisplayName
            }
        };
    }

    public async Task<CreateIssueResponse> CreateIssueAsync(CreateIssueRequest request, string supabaseUserId)
    {
        var user = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            throw new ArgumentException("User not found");

        var issue = new Issue
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            Address = request.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            LocationAccuracy = request.LocationAccuracy,
            Neighborhood = request.Neighborhood,
            Landmark = request.Landmark,
            Urgency = request.Urgency,
            Status = IssueStatus.Submitted,
            CurrentSituation = request.CurrentSituation,
            DesiredOutcome = request.DesiredOutcome,
            CommunityImpact = request.CommunityImpact,
            AIGeneratedDescription = request.AIGeneratedDescription,
            AIProposedSolution = request.AIProposedSolution,
            AIConfidence = request.AIConfidence,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Issues.Add(issue);

        // Add photos
        if (request.Photos?.Any() == true)
        {
            foreach (var photoRequest in request.Photos)
            {
                var photo = new IssuePhoto
                {
                    Id = Guid.NewGuid(),
                    IssueId = issue.Id,
                    Url = photoRequest.Url,
                    ThumbnailUrl = photoRequest.ThumbnailUrl,
                    Caption = photoRequest.Caption,
                    Quality = photoRequest.Quality,
                    CreatedAt = DateTime.UtcNow
                };
                _context.IssuePhotos.Add(photo);
            }
        }

        await _context.SaveChangesAsync();

        // Award points for issue creation
        await _gamificationService.AwardPointsAsync(user.Id, 50, "Issue created");

        _logger.LogInformation("Issue created successfully: {IssueId} by user {UserId}", issue.Id, user.Id);

        return new CreateIssueResponse
        {
            Id = issue.Id,
            Status = issue.Status,
            CreatedAt = issue.CreatedAt
        };
    }

    public async Task<bool> TrackEmailSentAsync(TrackEmailRequest request, string supabaseUserId)
    {
        var issue = await _context.Issues
            .Include(i => i.User)
            .FirstOrDefaultAsync(i => i.Id == request.IssueId);

        if (issue == null)
            return false;

        var user = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            return false;

        // Check if user already sent email for this issue
        var existingTracking = await _context.EmailTrackings
            .FirstOrDefaultAsync(et => et.IssueId == request.IssueId && et.UserId == user.Id);

        if (existingTracking != null)
            return false; // Already tracked

        // Add email tracking
        var emailTracking = new EmailTracking
        {
            Id = Guid.NewGuid(),
            IssueId = request.IssueId,
            UserId = user.Id,
            AuthorityEmail = request.AuthorityEmail,
            SentAt = DateTime.UtcNow
        };

        _context.EmailTrackings.Add(emailTracking);

        // Increment email count
        issue.EmailsSent++;
        issue.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Award points for sending email
        await _gamificationService.AwardPointsAsync(user.Id, 5, "Email sent");

        _logger.LogInformation("Email tracked for issue {IssueId} by user {UserId}", request.IssueId, user.Id);

        return true;
    }
}
```

### Step 5: Authentication Integration

#### Services/AuthService.cs
```csharp
using Civica.Api.Data;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Civica.Api.Services;

public class AuthService : IAuthService
{
    private readonly CivicaDbContext _context;
    private readonly ISupabaseService _supabaseService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(CivicaDbContext context, ISupabaseService supabaseService, ILogger<AuthService> logger)
    {
        _context = context;
        _supabaseService = supabaseService;
        _logger = logger;
    }

    public async Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId)
    {
        var user = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            return null;

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
            Points = user.Points,
            Level = user.Level,
            CreatedAt = user.CreatedAt,
            EmailVerified = user.EmailVerified
        };
    }

    public async Task<UserProfileResponse> CreateUserProfileAsync(CreateUserProfileRequest request, string supabaseUserId, string email)
    {
        var existingUser = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (existingUser != null)
        {
            throw new ArgumentException("User profile already exists");
        }

        var user = new UserProfile
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = supabaseUserId,
            Email = email,
            DisplayName = request.DisplayName,
            PhotoUrl = request.PhotoUrl,
            County = request.County ?? "București",
            City = request.City ?? "București", 
            District = request.District ?? "Sector 5",
            ResidenceType = request.ResidenceType.HasValue ? 
                Enum.Parse<Domain.ResidenceType>(request.ResidenceType.Value.ToString()) : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            EmailVerified = true // Supabase handles verification
        };

        _context.UserProfiles.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User profile created: {UserId} for Supabase user {SupabaseUserId}", 
            user.Id, supabaseUserId);

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
            Points = user.Points,
            Level = user.Level,
            CreatedAt = user.CreatedAt,
            EmailVerified = user.EmailVerified
        };
    }

    public async Task<UserProfileResponse> UpdateUserProfileAsync(UpdateUserProfileRequest request, string supabaseUserId)
    {
        var user = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            throw new ArgumentException("User not found");

        // Update fields
        if (!string.IsNullOrEmpty(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.PhotoUrl))
            user.PhotoUrl = request.PhotoUrl;

        if (request.ResidenceType.HasValue)
            user.ResidenceType = Enum.Parse<Domain.ResidenceType>(request.ResidenceType.Value.ToString());

        if (request.IssueUpdatesEnabled.HasValue)
            user.IssueUpdatesEnabled = request.IssueUpdatesEnabled.Value;

        if (request.CommunityNewsEnabled.HasValue)
            user.CommunityNewsEnabled = request.CommunityNewsEnabled.Value;

        if (request.MonthlyDigestEnabled.HasValue)
            user.MonthlyDigestEnabled = request.MonthlyDigestEnabled.Value;

        if (request.AchievementsEnabled.HasValue)
            user.AchievementsEnabled = request.AchievementsEnabled.Value;

        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

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
            Points = user.Points,
            Level = user.Level,
            CreatedAt = user.CreatedAt,
            EmailVerified = user.EmailVerified
        };
    }
}
```

### Step 6: API Endpoints

#### Endpoints/IssueEndpoints.cs
```csharp
using Microsoft.AspNetCore.Authorization;
using Civica.Api.Models.Requests.Issues;
using Civica.Api.Services.Interfaces;
using Civica.Api.Infrastructure.Constants;
using System.Security.Claims;

namespace Civica.Api.Endpoints;

public static class IssueEndpoints
{
    public static void MapIssueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Issues.Base)
            .WithTags("Issues")
            .WithOpenApi();

        // GET /api/issues
        group.MapGet("/", async (
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            [FromQuery] string? category = null,
            [FromQuery] string? urgency = null,
            [FromQuery] string? district = null,
            [FromQuery] string sortBy = "date",
            [FromQuery] bool sortDescending = true,
            IIssueService issueService) =>
        {
            var request = new GetIssuesRequest
            {
                Page = page,
                PageSize = Math.Min(pageSize, 50), // Max 50 items per page
                Category = Enum.TryParse<IssueCategory>(category, true, out var cat) ? cat : null,
                Urgency = Enum.TryParse<UrgencyLevel>(urgency, true, out var urg) ? urg : null,
                District = district,
                SortBy = sortBy,
                SortDescending = sortDescending
            };

            var result = await issueService.GetAllIssuesAsync(request);
            return Results.Ok(result);
        })
        .WithName("GetIssues")
        .WithSummary("Get paginated list of approved issues")
        .Produces<PagedResult<IssueListResponse>>()
        .Produces(400);

        // GET /api/issues/{id}
        group.MapGet("/{id:guid}", async (Guid id, IIssueService issueService) =>
        {
            var issue = await issueService.GetIssueByIdAsync(id);
            return issue != null ? Results.Ok(issue) : Results.NotFound();
        })
        .WithName("GetIssueById")
        .WithSummary("Get issue details by ID")
        .Produces<IssueDetailResponse>()
        .Produces(404);

        // POST /api/issues
        group.MapPost("/", [Authorize] async (
            CreateIssueRequest request,
            HttpContext context,
            IIssueService issueService) =>
        {
            var supabaseUserId = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(supabaseUserId))
                return Results.Unauthorized();

            try
            {
                var result = await issueService.CreateIssueAsync(request, supabaseUserId);
                return Results.Created($"/api/issues/{result.Id}", result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .WithName("CreateIssue")
        .WithSummary("Create a new issue (requires authentication)")
        .Produces<CreateIssueResponse>(201)
        .Produces(400)
        .Produces(401);

        // PUT /api/issues/{id}/email-sent
        group.MapPut("/{id:guid}/email-sent", [Authorize] async (
            Guid id,
            TrackEmailRequest request,
            HttpContext context,
            IIssueService issueService) =>
        {
            var supabaseUserId = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(supabaseUserId))
                return Results.Unauthorized();

            request.IssueId = id;
            var success = await issueService.TrackEmailSentAsync(request, supabaseUserId);
            
            return success ? 
                Results.Ok(new { Success = true, Message = "Email tracked successfully" }) :
                Results.BadRequest(new { Success = false, Message = "Failed to track email" });
        })
        .WithName("TrackEmailSent")
        .WithSummary("Track that user sent an email for this issue")
        .Produces<object>()
        .Produces(400)
        .Produces(401);
    }
}
```

## 🚀 Deployment Configuration

### Railway Setup

Create a `railway.toml` file:

```toml
[build]
builder = "nixpacks"
buildCommand = "dotnet publish Civica.Api/Civica.Api.csproj -c Release -o out"

[deploy]
startCommand = "dotnet out/Civica.Api.dll"
healthcheckPath = "/health"
healthcheckTimeout = 300
restartPolicyType = "on_failure"
restartPolicyMaxRetries = 3

[environments.production]
variables = { ASPNETCORE_ENVIRONMENT = "Production" }
```

### GitHub Actions Workflow (Optional)

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to Railway

on:
  push:
    branches: [ main ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
        
    - name: Restore dependencies
      run: dotnet restore Civica.Api/Civica.Api.csproj
      
    - name: Build
      run: dotnet build Civica.Api/Civica.Api.csproj --no-restore -c Release
      
    - name: Test
      run: dotnet test --no-build -c Release
      
    - name: Deploy to Railway
      uses: railwayapp/railway-deploy@v1
      with:
        railway_token: ${{ secrets.RAILWAY_TOKEN }}
        service: 'civica-api'
```

## 🧪 Testing Strategy

### Integration Tests Example

```csharp
[TestClass]
public class IssueEndpointsTests : IntegrationTestBase
{
    [TestMethod]
    public async Task GetIssues_ReturnsPagedResult()
    {
        // Arrange
        await SeedTestData();
        
        // Act
        var response = await _client.GetAsync("/api/issues?page=1&pageSize=10");
        
        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult<IssueListResponse>>(content);
        
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Items.Count > 0);
    }
    
    [TestMethod]
    public async Task CreateIssue_WithValidData_ReturnsCreated()
    {
        // Arrange
        var token = await GetValidJwtToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var request = new CreateIssueRequest
        {
            Title = "Test Issue",
            Description = "Test description",
            Category = IssueCategory.Infrastructure,
            Address = "Test Address",
            Latitude = 44.4268,
            Longitude = 26.1025,
            Urgency = UrgencyLevel.Medium
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/issues", request);
        
        // Assert
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
```

## 📋 Implementation Checklist

### Phase 1: Core Setup
- [ ] Create .NET 9 project structure
- [ ] Configure Program.cs with all middleware
- [ ] Set up PostgreSQL connection
- [ ] Configure Supabase JWT authentication
- [ ] Create database context and initial migrations
- [ ] Implement error handling middleware
- [ ] Set up CORS for Angular frontend

### Phase 2: Authentication & Users
- [ ] Implement SupabaseService for JWT validation
- [ ] Create AuthService for user profile management
- [ ] Build UserService for profile operations
- [ ] Add AuthEndpoints for profile CRUD
- [ ] Add UserEndpoints for gamification data
- [ ] Test Supabase integration end-to-end

### Phase 3: Issues Management
- [ ] Implement IssueService with all CRUD operations
- [ ] Create IssueEndpoints with pagination and filtering
- [ ] Add photo upload handling (if needed)
- [ ] Implement email tracking functionality
- [ ] Add AI integration placeholders
- [ ] Test all issue operations

### Phase 4: Admin & Gamification
- [ ] Implement AdminService for approval workflows
- [ ] Create AdminEndpoints for issue moderation
- [ ] Build GamificationService for points/badges
- [ ] Add GamificationEndpoints for achievements
- [ ] Implement bulk operations for admin
- [ ] Test admin and gamification features

### Phase 5: Deployment & Integration
- [ ] Configure Railway deployment
- [ ] Set up environment variables
- [ ] Test production deployment
- [ ] Update frontend to use real API endpoints
- [ ] Perform integration testing with Angular app
- [ ] Monitor and optimize performance

## 🔍 Testing with Frontend

1. **Start the API**: `dotnet run --project Civica.Api`
2. **Update Angular environment**: Point `apiUrl` to `http://localhost:5000/api`
3. **Test each endpoint** systematically:
   - Authentication flow with Supabase
   - Issue listing and filtering
   - Issue creation and photo upload
   - Email tracking functionality
   - User profile management
   - Admin approval workflow

## 📚 Additional Resources

- [.NET 9 Minimal APIs Documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [Supabase C# Client Documentation](https://github.com/supabase-community/supabase-csharp)
- [Entity Framework Core Documentation](https://docs.microsoft.com/en-us/ef/core/)
- [Railway Deployment Guide](https://docs.railway.app/deploy/deployments)

## 🎯 Success Criteria

The backend is successful when:
- ✅ All Angular mock services are replaced with real API calls
- ✅ Supabase authentication works end-to-end
- ✅ Database schema supports all frontend features
- ✅ API performance meets frontend requirements (<200ms average)
- ✅ Error handling provides meaningful feedback
- ✅ Railway deployment is stable and scalable
- ✅ No regressions in frontend functionality

---

**Next Steps**: Follow this guide step-by-step, implementing each phase completely before moving to the next. The frontend team is ready to integrate as soon as endpoints are available.