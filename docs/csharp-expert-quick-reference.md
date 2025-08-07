# csharp-expert Quick Reference Guide

## 🎯 Implementation Context

**Your Mission**: Build a .NET 9 Minimal API backend that seamlessly replaces Angular mock services while maintaining 100% feature compatibility.

**Current State**:
- ✅ Frontend: Production-ready Angular app with comprehensive mock services
- ✅ Architecture: Completely designed and documented
- ✅ Database Schema: Full PostgreSQL schema with all tables defined
- ✅ API Specification: All 25+ endpoints documented with request/response schemas
- 🚧 Your Task: Implement the backend following existing documentation

## 📚 Essential Documentation (Read These First)

1. **`docs/api/backend-implementation-guide.md`** - Complete step-by-step implementation guide
2. **`docs/api/database-schema.md`** - Full PostgreSQL schema with all tables and relationships  
3. **`docs/api/api-specification.md`** - All 25+ API endpoints with exact request/response formats
4. **`docs/technical/backend-architecture.md`** - Architecture decisions and patterns
5. **`docs/api/csharp-expert-implementation-roadmap.md`** - Your 14-day implementation plan

## 🚀 Critical Implementation Order

### Phase 1: Foundation (Days 1-2)
```csharp
// 1. Create project structure exactly as documented
// 2. Configure Program.cs with Supabase Auth + EF Core
// 3. Implement CivicaDbContext with all entities
// 4. Test database connection and health check

var builder = WebApplication.CreateBuilder(args);

// Supabase Auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Supabase:Url"];
        options.Audience = "authenticated";
    });

// EF Core PostgreSQL
builder.Services.AddDbContext<CivicaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));
```

### Phase 2: User System (Days 3-4)  
```csharp
// Critical endpoints to replace MockAuthService:
GET /api/auth/profile          // Get user profile
POST /api/auth/profile         // Create profile after Supabase registration  
PUT /api/auth/profile          // Update user profile

// Key pattern - extract user from JWT:
public static string GetUserId(this ClaimsPrincipal user)
{
    return user.FindFirst("sub")?.Value 
        ?? throw new UnauthorizedAccessException("User not authenticated");
}
```

### Phase 3: Issue System (Days 5-7)
```csharp
// Critical endpoints to replace MockDataService & MockIssueCreationService:
GET /api/issues                     // List with pagination/filtering
POST /api/issues                    // Create new issue
GET /api/issues/{id}                // Get issue details  
PUT /api/issues/{id}/email-sent     // Increment email counter

// Example endpoint implementation:
app.MapPost("/api/issues", async (
    CreateIssueRequest request,
    IIssueService issueService,
    HttpContext context) =>
{
    var userId = context.User.GetUserId();
    var issue = await issueService.CreateAsync(request, userId);
    return Results.Created($"/api/issues/{issue.Id}", issue);
})
.RequireAuthorization()
.WithName("CreateIssue")
.WithOpenApi();
```

### Phase 4: Gamification (Days 8-10)
```csharp
// Critical endpoints to replace MockUserService:
GET /api/user/stats               // Points, level, badges  
GET /api/user/badges              // User's earned badges
GET /api/user/achievements        // User achievements
GET /api/user/leaderboard        // Leaderboard data

// Auto-award points on actions:
await _gamificationService.AwardPointsAsync(userId, 50, "issue_created");
```

### Phase 5: Admin System (Days 11-12)
```csharp
// Critical endpoints to replace MockAdminService:
GET /api/admin/pending-issues     // Issues awaiting approval
PUT /api/admin/issues/{id}/approve // Approve issue
PUT /api/admin/issues/{id}/reject  // Reject issue
GET /api/admin/statistics         // Admin dashboard stats
```

## 🏗️ Key Models & Entities

### User Profile
```csharp
public class UserProfile
{
    public Guid Id { get; set; }
    public Guid SupabaseUserId { get; set; } // Links to auth.users
    public string DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
    public string County { get; set; }
    public string City { get; set; }
    public string District { get; set; }
    public string ResidenceType { get; set; }
    public int Points { get; set; } = 0;
    public int Level { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual ICollection<Issue> Issues { get; set; }
    public virtual ICollection<UserBadge> UserBadges { get; set; }
}
```

### Issue 
```csharp
public class Issue
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Category { get; set; } // roads, lighting, sanitation, parks, safety, other
    public LocationData Location { get; set; } // JSON stored in PostgreSQL
    public IssueStatus Status { get; set; } = IssueStatus.Pending;
    public int EmailsSent { get; set; } = 0;
    public string? AiGeneratedText { get; set; }
    public string? AdminNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual UserProfile User { get; set; }
    public virtual ICollection<IssuePhoto> Photos { get; set; }
    public virtual ICollection<AdminAction> AdminActions { get; set; }
}

public enum IssueStatus
{
    Draft,
    Pending, 
    Approved,
    Rejected,
    InProgress,
    Resolved
}
```

## 🔧 Essential Configuration

### appsettings.json
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Server=localhost;Database=civica;Username=postgres;Password=your_password"
  },
  "Supabase": {
    "Url": "https://your-project.supabase.co",
    "AnonKey": "your-anon-key",
    "ServiceKey": "your-service-key"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### CORS Configuration
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("CivicaPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://civica-frontend.vercel.app")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

app.UseCors("CivicaPolicy");
```

## 🎯 Frontend Integration Points

### Mock Service Replacement Strategy
1. **MockAuthService** → AuthEndpoints (user profile management)
2. **MockDataService** → IssueEndpoints (issue CRUD, email tracking) 
3. **MockUserService** → UserEndpoints + GamificationEndpoints (stats, badges)
4. **MockIssueCreationService** → IssueEndpoints (issue creation workflow)
5. **MockAdminService** → AdminEndpoints (approval workflows)

### Data Structure Compatibility
**CRITICAL**: Your API responses must match the frontend interfaces exactly:

```typescript
// Frontend expects this structure - match it exactly
export interface IssueResponse {
  id: string;
  title: string;
  description: string;
  category: string;
  location: {
    address: string;
    coordinates: { lat: number; lng: number };
    accuracy: number;
  };
  photos: string[];
  status: 'draft' | 'pending' | 'approved' | 'rejected' | 'in_progress' | 'resolved';
  emailsSent: number;
  userId: string;
  createdAt: string;
  updatedAt: string;
}
```

## 🚨 Critical Success Factors

### 1. Authentication Flow
- Validate Supabase JWT on all protected endpoints
- Extract user ID from JWT claims correctly  
- Sync user profiles between Supabase and local database
- Handle token refresh seamlessly

### 2. Data Persistence
- All user actions must persist correctly
- Issue photos stored with proper metadata
- Email counters increment accurately
- Gamification points calculate correctly

### 3. Performance Targets  
- Health check: <50ms
- Public endpoints: <200ms (P95)
- Authenticated endpoints: <300ms (P95)
- Database queries: <100ms typical

### 4. Error Handling
```csharp
public class ErrorHandlingMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
        }
    }
}
```

## 📦 Required NuGet Packages

```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.0" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.4.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
<PackageReference Include="FluentValidation.AspNetCore" Version="11.3.0" />
<PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="7.0.3" />
```

## 🚀 Railway Deployment

### Environment Variables (Set in Railway)
```env
DATABASE_URL=postgresql://postgres:password@localhost:5432/civica
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_ANON_KEY=your-anon-key
SUPABASE_SERVICE_KEY=your-service-key
ASPNETCORE_ENVIRONMENT=Production
PORT=8080
```

### Startup Configuration for Railway
```csharp
// Ensure migrations run on startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();
    await context.Database.MigrateAsync();
}

// Use Railway's assigned port
app.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");
```

## ✅ Implementation Checklist

### Foundation
- [ ] .NET 9 project created with proper structure
- [ ] All required NuGet packages installed
- [ ] Database connection working
- [ ] Supabase Auth configured
- [ ] Health check endpoint responds
- [ ] Swagger UI accessible

### Core Features
- [ ] User profile creation/retrieval/update works
- [ ] Issue CRUD operations functional
- [ ] Photo upload and storage working  
- [ ] Email counter increment working
- [ ] Gamification point calculations correct
- [ ] Badge earning triggers working
- [ ] Admin approval workflow functional

### Integration & Deployment
- [ ] All frontend mock services replaced
- [ ] No regressions in frontend functionality
- [ ] CORS configured correctly
- [ ] Railway deployment successful
- [ ] Production database working
- [ ] Performance targets met
- [ ] Error handling comprehensive

## 🆘 Common Issues & Solutions

### Supabase Auth Issues
```csharp
// Common issue: JWT validation fails
// Solution: Ensure correct Authority and Audience
options.Authority = "https://your-project.supabase.co";
options.Audience = "authenticated"; // Not your anon key!
```

### EF Core Migration Issues
```bash
# Common issue: Migration fails on Railway
# Solution: Ensure connection string format is correct
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### CORS Issues
```csharp
// Common issue: Frontend can't connect
// Solution: Add both development and production origins
.WithOrigins("http://localhost:4200", "https://your-frontend.vercel.app")
```

## 🎯 Final Success Criteria

Your backend is complete when:
1. ✅ All frontend features work identically to mock version
2. ✅ User registration and authentication flow seamless  
3. ✅ Issue creation, viewing, and management functional
4. ✅ Gamification system calculates correctly
5. ✅ Admin approval workflows working
6. ✅ Production deployment on Railway successful
7. ✅ Performance targets met (<200ms API responses)
8. ✅ No data loss or corruption
9. ✅ Comprehensive error handling and logging
10. ✅ Documentation updated with implementation notes

**Remember**: The frontend team is ready and waiting. Your backend should be a drop-in replacement for the mock services with zero regressions in functionality!