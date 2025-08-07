# Civica Backend Implementation Roadmap for csharp-expert

## 🎯 Executive Summary

This roadmap provides the **csharp-expert agent** with a clear, step-by-step plan to implement the Civica .NET 9 Minimal API backend. All architectural decisions have been made, and the frontend is ready for integration.

**⚡ Critical Context**:
- Frontend is **production-ready** with comprehensive mock services
- All backend architecture has been **designed and documented**
- Database schema is **complete** with full specifications
- API contracts are **defined** and match frontend expectations
- Your job: **Implement the designed backend following this roadmap**

## 📋 Prerequisites Checklist

Before starting implementation:
- [ ] Read `docs/api/backend-implementation-guide.md` (complete step-by-step guide)
- [ ] Review `docs/api/api-specification.md` (all endpoint specifications) 
- [ ] Study `docs/technical/backend-architecture.md` (architectural decisions)
- [ ] Understand `docs/project/Implementation.md` (project requirements)
- [ ] Access to Railway account for PostgreSQL and deployment
- [ ] Supabase project credentials for authentication

## 🚀 Implementation Phases

### Phase 1: Foundation Setup (Week 1, Days 1-2)

#### Day 1: Project Structure & Configuration
```bash
# Create project structure exactly as documented
mkdir -p Civica.Api/{Endpoints,Services/Interfaces,Models/{Domain,Requests,Responses},Data/{Configurations,Migrations},Infrastructure/{Middleware,Extensions,Validators,Constants}}
```

**Key Tasks**:
1. **Create .NET 9 project** with exact package dependencies from implementation guide
2. **Set up `Program.cs`** with Minimal API configuration, CORS, and middleware
3. **Configure `appsettings.json`** with Railway PostgreSQL and Supabase settings
4. **Implement `CivicaDbContext`** with all entity configurations
5. **Create initial migration** and test database connection

**Success Criteria**:
- [ ] Project builds without errors
- [ ] Database connection established
- [ ] Health check endpoint responds
- [ ] Swagger UI accessible at `/swagger`

#### Day 2: Authentication Foundation
**Key Tasks**:
1. **Configure Supabase Auth** in `Program.cs` with JWT validation
2. **Create `SupabaseService`** for token validation and user sync
3. **Implement authentication middleware** and user context extraction
4. **Set up basic error handling middleware**
5. **Create first endpoint**: `GET /api/health` (no auth required)

**Success Criteria**:
- [ ] JWT validation working with Supabase tokens
- [ ] Authentication middleware correctly extracts user ID
- [ ] Error handling returns structured responses
- [ ] Basic logging implemented

### Phase 2: Core User System (Week 1, Days 3-4)

#### Day 3: User Profile System
**Implementation Order**:
1. **User Profile Models** (`User.cs`, requests/responses)
2. **UserService** with profile CRUD operations
3. **AuthEndpoints** for profile management
4. **Database operations** with proper error handling

**Critical Endpoints to Implement**:
```csharp
// Replace frontend MockAuthService calls
GET /api/auth/profile          // Get user profile
POST /api/auth/profile         // Create user profile (after Supabase registration)
PUT /api/auth/profile          // Update user profile
```

**Success Criteria**:
- [ ] User can create profile after Supabase registration
- [ ] Profile data persists correctly in PostgreSQL
- [ ] Frontend can retrieve and update user profiles
- [ ] Proper error handling for missing/invalid profiles

#### Day 4: Authentication Integration Testing
**Key Tasks**:
1. **Test Supabase JWT validation** with real tokens
2. **Verify user profile sync** between Supabase and PostgreSQL
3. **Test frontend integration** by replacing MockAuthService calls
4. **Implement proper CORS** for Angular development server
5. **Add comprehensive logging** for authentication flows

### Phase 3: Core Issue System (Week 1, Days 5-7)

#### Day 5: Issue Domain Models
**Implementation Order**:
1. **Issue Models** (`Issue.cs`, `IssuePhoto.cs`, DTOs)
2. **Issue Entity Configurations** with proper indexing
3. **IssueService Interface** matching frontend mock service
4. **Database migration** for issue-related tables

#### Day 6: Issue CRUD Operations
**Critical Endpoints**:
```csharp
// Replace frontend MockIssueCreationService and MockDataService
GET /api/issues                       // List issues with filtering/pagination
POST /api/issues                      // Create new issue
GET /api/issues/{id}                  // Get issue details
PUT /api/issues/{id}                  // Update issue
PUT /api/issues/{id}/email-sent       // Track email sent (increment counter)
```

**Frontend Integration Points**:
- Exact same data structures as mock services
- Pagination support for issues list
- Image upload handling for issue photos
- Location data storage and retrieval
- Issue status management (draft, submitted, approved, etc.)

#### Day 7: Issue System Testing & Frontend Integration
**Key Tasks**:
1. **Replace MockDataService** calls in Angular components
2. **Test issue creation workflow** end-to-end
3. **Verify photo upload and storage** functionality
4. **Test pagination and filtering** on issues list
5. **Validate location data** storage and retrieval

### Phase 4: User Dashboard & Gamification (Week 2, Days 1-3)

#### Day 8-9: Gamification System
**Implementation Priority**:
1. **Badge and Achievement Models** with relationships
2. **GamificationService** for points, levels, badges
3. **GamificationEndpoints** for user stats
4. **Automated point awards** on issue creation/approval

**Critical Endpoints**:
```csharp
// Replace frontend MockUserService calls
GET /api/user/stats                   // Get user statistics (points, level, badges)
GET /api/user/badges                  // Get user's earned badges
GET /api/user/achievements            // Get user's achievements
GET /api/user/leaderboard            // Get leaderboard data
POST /api/user/achievements/{id}/claim // Claim achievement
```

#### Day 10: Dashboard Data Integration
**Key Tasks**:
1. **Replace MockUserService** in Angular dashboard
2. **Test gamification calculations** (points, levels, badges)
3. **Verify dashboard statistics** display correctly
4. **Implement achievement triggering** on user actions

### Phase 5: Admin System (Week 2, Days 4-5)

#### Day 11: Admin Workflows
**Critical Endpoints**:
```csharp
// Replace frontend MockAdminService calls
GET /api/admin/pending-issues         // Get issues awaiting approval
PUT /api/admin/issues/{id}/approve    // Approve issue
PUT /api/admin/issues/{id}/reject     // Reject issue with reason
GET /api/admin/statistics             // Admin dashboard statistics
POST /api/admin/bulk-approve          // Bulk approve issues
```

#### Day 12: Admin Integration Testing
**Key Tasks**:
1. **Replace MockAdminService** in Angular admin interface
2. **Test approval/rejection workflows**
3. **Verify admin statistics** calculations
4. **Test bulk operations** functionality

### Phase 6: Production Deployment (Week 2, Days 6-7)

#### Day 13: Railway Deployment
**Key Tasks**:
1. **Configure Railway environment** variables
2. **Set up production database** with migrations
3. **Deploy initial version** to Railway
4. **Configure CORS** for production Angular domain
5. **Test production authentication** with Supabase

#### Day 14: Production Validation & Frontend Integration
**Key Tasks**:
1. **Update Angular environment** to use production API
2. **Full end-to-end testing** of all workflows
3. **Performance testing** and optimization
4. **Security validation** and penetration testing
5. **Monitoring and alerting setup**

## 🔧 Implementation Guidelines

### Code Quality Standards
```csharp
// Follow these patterns consistently

// 1. Minimal API Endpoint Pattern
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
.WithOpenApi()
.WithSummary("Create a new civic issue");

// 2. Service Pattern
public class IssueService : IIssueService
{
    private readonly CivicaDbContext _context;
    private readonly IGamificationService _gamification;
    private readonly ILogger<IssueService> _logger;

    public async Task<IssueResponse> CreateAsync(CreateIssueRequest request, string userId)
    {
        try
        {
            var issue = new Issue
            {
                Id = Guid.NewGuid(),
                UserId = Guid.Parse(userId),
                Title = request.Title,
                Description = request.Description,
                // Map all properties...
                CreatedAt = DateTime.UtcNow
            };

            _context.Issues.Add(issue);
            await _context.SaveChangesAsync();

            // Award points for issue creation
            await _gamification.AwardPointsAsync(userId, 50, "issue_created");

            _logger.LogInformation("Issue {IssueId} created by user {UserId}", issue.Id, userId);
            
            return issue.ToResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create issue for user {UserId}", userId);
            throw;
        }
    }
}

// 3. Error Handling Pattern
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
            await HandleValidationException(context, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            await HandleUnauthorizedException(context, ex);
        }
        catch (Exception ex)
        {
            await HandleGenericException(context, ex);
        }
    }
}
```

### Database Patterns
```csharp
// Entity Configuration Example
public class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.HasKey(i => i.Id);
        
        builder.Property(i => i.Title)
            .HasMaxLength(500)
            .IsRequired();
            
        builder.Property(i => i.Description)
            .HasMaxLength(5000);
            
        builder.Property(i => i.Location)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<LocationData>(v, (JsonSerializerOptions)null))
            .HasColumnType("jsonb");
            
        builder.HasOne<UserProfile>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => new { i.CreatedAt, i.Status });
        builder.HasIndex(i => i.UserId);
    }
}
```

### Frontend Integration Checklist

For each service replacement:
- [ ] **Exact Data Structures**: Maintain identical interfaces and response formats
- [ ] **Error Handling**: Return proper HTTP status codes and error messages
- [ ] **Validation**: Implement the same validation rules as frontend
- [ ] **Performance**: Maintain or improve response times vs mock services
- [ ] **CORS**: Configure properly for Angular development and production

## 📊 Success Metrics

### Technical Metrics
- [ ] **Response Time**: <200ms average for API endpoints
- [ ] **Error Rate**: <1% for all endpoints
- [ ] **Database Performance**: <100ms for typical queries
- [ ] **Memory Usage**: <200MB baseline
- [ ] **Startup Time**: <10 seconds including migrations

### Functional Metrics
- [ ] **All frontend features work** exactly as they did with mocks
- [ ] **No regression** in user experience
- [ ] **Data persistence** works correctly
- [ ] **Authentication flow** seamless end-to-end
- [ ] **Admin workflows** function properly
- [ ] **Gamification system** calculates correctly

## 🚨 Critical Implementation Notes

### Must-Follow Patterns
1. **Authentication**: Always validate Supabase JWT and extract user ID correctly
2. **Error Handling**: Use structured error responses with proper HTTP status codes
3. **Logging**: Log all significant operations with user context
4. **Validation**: Validate all inputs using FluentValidation or similar
5. **Performance**: Use async/await throughout for all I/O operations
6. **Security**: Apply proper authorization checks on all endpoints

### Frontend Integration Priority
1. **Authentication Endpoints First**: Users must be able to register and login
2. **Core Issue Endpoints**: Issue creation and listing are critical paths
3. **User Dashboard**: Gamification data for user engagement
4. **Admin Interface**: Last priority but must work for content moderation

### Railway Deployment Considerations
- **Environment Variables**: Store all secrets in Railway environment
- **Database Migrations**: Run automatically on deployment
- **Health Checks**: Implement comprehensive health checks
- **Logging**: Configure structured logging for Railway logs
- **CORS**: Properly configure for Angular frontend domain

## 📝 Documentation Requirements

As you implement, update these files:
- [ ] `docs/api/api-specification.md` - Document any changes to API contracts
- [ ] `docs/technical/backend-architecture.md` - Update with implementation decisions
- [ ] `docs/project/Implementation.md` - Update backend implementation status
- [ ] Create `docs/deployment/railway-deployment.md` - Document deployment process

## 🎯 Final Checklist

Before marking the backend complete:
- [ ] All frontend mock services replaced with real API calls
- [ ] Full user registration and authentication flow working
- [ ] Issue creation, viewing, and management working
- [ ] Admin approval interface functional
- [ ] Gamification system calculating correctly
- [ ] Production deployment successful on Railway
- [ ] Performance targets met (<200ms response times)
- [ ] Error handling comprehensive and user-friendly
- [ ] Security validation passed
- [ ] Documentation updated with implementation details

## 🚀 Quick Start Commands

```bash
# Create new .NET 9 project
dotnet new web -n Civica.Api --framework net8.0

# Add required packages (see implementation guide for complete list)
dotnet add package Microsoft.EntityFrameworkCore.Npgsql
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Swashbuckle.AspNetCore

# Create and run initial migration
dotnet ef migrations add InitialCreate
dotnet ef database update

# Run the application
dotnet run
```

## 🔄 Next Steps

1. **Start with Phase 1** - Foundation setup is critical for everything else
2. **Follow the exact order** - Each phase builds on the previous
3. **Test integration continuously** - Replace frontend mocks incrementally
4. **Monitor performance** - Ensure response times meet targets
5. **Document as you go** - Update relevant documentation files

**Remember**: The frontend team is waiting for a working backend that seamlessly replaces their mock services. Your implementation should maintain all existing functionality while adding real data persistence and authentication.