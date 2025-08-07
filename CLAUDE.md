# CLAUDE.md - Civica Project Intelligence

This file provides essential context for Claude agents working on the Civica project. It references and coordinates all project documentation to ensure consistent, high-quality development.

## 🎯 Project Overview

**Civica** is a civic engagement platform for Romanian citizens to collectively pressure local authorities through coordinated email campaigns. 

### Current Project State
- **Frontend**: ✅ READY FOR INTEGRATION - Angular application with mock services, ready to connect to backend
- **Backend**: 🚧 IN DEVELOPMENT - .NET 9 Minimal API with Supabase Auth and PostgreSQL/MongoDB

The frontend is feature-complete with mock services but will continue to evolve as we:
- Integrate with the real backend API
- Add new features based on backend capabilities
- Refine UI/UX based on real data and user feedback
- Optimize performance with actual API responses

### Completed Features (December 2024)
- **User Registration System** with Google OAuth and email/password options (mocked)
- **Issue Creation Workflow** where registered users can report civic issues
- **AI-Enhanced Descriptions** (mocked) for issue text generation
- **Gamification System** with points, badges, and achievements
- **Admin Approval Interface** for issue moderation

### Backend Tech Stack (Next Phase)
- **.NET 9 Minimal API** with C# 13
- **Supabase Auth** for authentication
- **PostgreSQL** (primary) or MongoDB
- **Railway** for deployment

## 📁 Critical Documentation Structure

**ALWAYS consult these files in order:**

1. **`docs/project/Implementation.md`** - Mandatory implementation standards, rules, and backend architecture
2. **`docs/design/ux.md`** - User journey specifications and flow requirements  
3. **`docs/design/Colour-Scheme.md`** - Color palette with CSS/SCSS variables
4. **`docs/design/Typography-Guide.md`** - Fira Sans implementation standards
5. **`docs/guides/Super_Claude_Docs.md`** - SuperClaude framework usage guide

**Additional Technical Docs:**
- **`docs/technical/GOOGLE_MAPS_SECURITY.md`** - Maps API security
- **`docs/technical/README_GOOGLE_MAPS.md`** - Maps integration guide
- **`docs/README.md`** - Documentation structure overview

**New Feature Documentation:**
- **`docs/design/user-registration-ux-research.md`** - UX research for user system
- **`docs/design/user-registration-ui-design.md`** - UI specifications for new features

### Documentation Update Protocol

**MANDATORY for ALL agents**: Documentation is a living part of the codebase and MUST be maintained.

When making ANY changes:
1. **Update existing MD files** in the `docs/` folder immediately after code changes
2. **Create new documentation** if no suitable file exists for your changes
3. **Keep all docs synchronized** - if your change affects multiple areas, update all relevant docs
4. **Use Task tool** to track documentation updates as part of your workflow
5. **Validate accuracy** before committing - documentation must match implementation

#### Documentation Structure
```
docs/
├── project/           # Project-wide documentation (Implementation.md, etc.)
├── design/           # UX/UI specifications and design decisions
├── technical/        # Technical guides and architecture docs
├── guides/           # How-to guides and tutorials
├── api/              # API documentation (to be created for backend)
└── deployment/       # Deployment and operations docs (to be created)
```

#### When to Update vs Create New Documentation
**Update existing files when:**
- Adding features to existing components
- Modifying existing workflows
- Changing implementation details
- Updating technical specifications

**Create new files when:**
- Adding entirely new systems or modules
- Documenting new architectural decisions
- Creating guides for new processes
- Adding API endpoint documentation

**File naming convention**: Use kebab-case (e.g., `backend-api-design.md`, `supabase-integration-guide.md`)

## 🚦 Development Workflow with Agents

### Primary Agents for Civica Development

```yaml
# Backend Development Team
backend-architect:
  Purpose: "Design backend architecture and development plans"
  Use for: API design, database schema, integration planning, development roadmap
  Workflow: Creates comprehensive plans for csharp-expert to implement

csharp-expert:
  Purpose: "Implement .NET 9 Minimal API backend"
  Use for: Writing C# code, implementing endpoints, Supabase integration, database operations
  Workflow: Follows plans from backend-architect, implements actual backend code

# Frontend Development Team  
frontend-developer:
  Purpose: "Build UI components with NG-ZORRO"
  Use for: Component implementation, responsive design, Angular patterns
  
ui-designer:
  Purpose: "Design system consistency and visual polish"
  Use for: Component styling, theme application, visual hierarchy
  
ux-researcher:
  Purpose: "Validate implementations against ux.md"
  Use for: User flow validation, usability testing, UX compliance

# Quality & Testing Team
test-writer-fixer:
  Purpose: "Comprehensive test coverage"
  Use for: Unit tests, integration tests, E2E tests, API tests
  
performance-benchmarker:
  Purpose: "Performance optimization and monitoring"
  Use for: Load testing, query optimization, bundle size reduction

# Support Agents
rapid-prototyper:
  Purpose: "Quick MVP features and experiments"
  Use for: New feature scaffolding, proof of concepts
  
brand-guardian:
  Purpose: "Ensure Civica brand consistency"
  Use for: Color compliance, typography standards, visual identity

devops-automator:
  Purpose: "CI/CD and deployment automation"
  Use for: Railway deployment, GitHub Actions, monitoring setup
```

### Supporting Agents

```yaml
mobile-app-builder:
  Use for: Mobile-specific optimizations, touch interactions
  
workflow-optimizer:
  Use for: Improving development processes, CI/CD setup
  
visual-storyteller:
  Use for: Creating compelling issue narratives, UI copy
  
whimsy-injector:
  Use for: Adding delightful micro-interactions, polish

romanian-translator:
  Use for: Translating UI elements, ensuring proper Romanian localization
```

### Backend Development Workflow

#### Phase 1: Architecture & Planning
```bash
# Backend architecture planning
"Use backend-architect agent to:
1. Analyze Implementation.md backend requirements
2. Design detailed API specification
3. Create database schema with migrations
4. Plan Supabase Auth integration
5. Define Railway deployment strategy
6. Create development roadmap for csharp-expert"
```

#### Phase 2: Core Implementation
```bash
# Backend implementation by csharp-expert
"Use csharp-expert agent to implement based on backend-architect's plan:
1. Create .NET 9 Minimal API project structure
2. Implement Supabase Auth integration
3. Build core API endpoints (auth, issues, users, admin)
4. Set up PostgreSQL with Entity Framework Core
5. Implement business logic and validation
6. Add comprehensive error handling"
```

#### Phase 3: Testing & Optimization
```bash
# Quality assurance workflow
"Coordinate multiple agents:
- Use test-writer-fixer to create API tests
- Use performance-benchmarker for load testing
- Use csharp-expert to fix any issues
- Use devops-automator for deployment setup"
```

### Frontend-Backend Integration Workflow

```bash
# Continuous integration approach
"As backend endpoints become available:
1. Use frontend-developer to update services incrementally
2. Replace mock calls with real API calls one at a time
3. Add error handling for real-world scenarios
4. Update UI based on actual API responses
5. Use test-writer-fixer for integration tests
6. Iterate based on findings"
```

### Frontend Enhancement Workflow

```bash
# Frontend continues to evolve
"While backend is being developed:
1. Use ui-designer to refine components based on feedback
2. Use frontend-developer to add new features
3. Use performance-benchmarker to optimize
4. Use ux-researcher to validate improvements
5. Keep mock services updated for unimplemented endpoints"
```

## 🎨 Design System Enforcement

### Color Usage (from Colour-Scheme.md)
```scss
// MANDATORY color variables
--oxford-blue: #14213D;  // Headers, navigation, trust
--orange-web: #FCA311;   // CTAs, urgency, email counters
--platinum: #E5E5E5;     // Backgrounds, borders
--white: #FFFFFF;        // Cards, surfaces
```

### Typography (from Typography-Guide.md)
```scss
// MANDATORY font implementation
--font-primary: "Fira Sans", sans-serif;
// Weights: 400 (body), 500 (nav), 600 (headers), 700 (h1), 800 (counters)
```

### Component Library
**Frontend**: NG-ZORRO components exclusively
**Backend**: .NET 9 Minimal API patterns

## 🏗️ Project Architecture

### Frontend Structure (Ready for Integration)
```
src/app/
├── components/        # UI components (NG-ZORRO based)
│   ├── auth/         # Registration, login components
│   ├── user/         # User dashboard with gamification
│   ├── issue-creation/ # Multi-step issue creation wizard
│   ├── admin/        # Admin approval interface
│   └── ...existing components
├── services/         # Mock services being replaced with real API calls
├── store/           # NgRx state management (will add new actions/effects)
└── config/          # Environment configuration for API endpoints
```

### Backend Structure (To Be Implemented)
```
Civica.Api/
├── Program.cs          # Minimal API entry point
├── Endpoints/          # API endpoint definitions
│   ├── AuthEndpoints.cs
│   ├── IssueEndpoints.cs
│   ├── UserEndpoints.cs
│   └── AdminEndpoints.cs
├── Services/           # Business logic
├── Models/            # Domain models
├── Data/              # EF Core context and migrations
└── Infrastructure/    # Cross-cutting concerns
```

## ⚠️ Critical Rules

### Current Phase Rules
- ✅ Frontend is READY - Continue development as needed while integrating
- 🚧 Backend development is ACTIVE - Building API to replace mock services
- 🔄 Integration is ITERATIVE - Frontend and backend evolve together
- 📝 Documentation must be updated with all changes

### Backend Development Rules
- ✅ Use .NET 9 Minimal API patterns
- ✅ Integrate Supabase for authentication
- ✅ Use PostgreSQL with Entity Framework Core
- ✅ Deploy to Railway platform
- ✅ Follow RESTful API design
- ✅ Implement comprehensive error handling
- ✅ Add OpenAPI/Swagger documentation

### Integration Rules
- ⚠️ Maintain API contracts defined in Implementation.md
- ⚠️ Preserve all frontend functionality
- ⚠️ Ensure backward compatibility
- ⚠️ Test thoroughly before replacing mocks

## 🔧 Agent-Specific Instructions

### For backend-architect Agent
- Study Implementation.md backend architecture section
- Design comprehensive development plan
- Consider Supabase Auth integration patterns
- Plan database schema with relationships
- Define clear API contracts
- Create actionable tasks for csharp-expert
- **Documentation**: Create `docs/api/api-design.md` for API specifications
- **Documentation**: Update Implementation.md with architectural decisions

### For csharp-expert Agent
- Follow backend-architect's development plan
- Use .NET 9 Minimal API best practices
- Implement async/await patterns throughout
- Use dependency injection properly
- Follow C# 13 conventions
- Write clean, maintainable code
- Add XML documentation comments
- **Documentation**: Create/update `docs/api/endpoints/` for each endpoint group
- **Documentation**: Create `docs/technical/backend-setup.md` for development setup
- **Documentation**: Update Implementation.md with implementation progress

### For frontend-developer Agent
- Continue adding features and improvements as needed
- Replace mock services incrementally as backend endpoints become available
- Add proper error handling for real API scenarios
- Update components based on actual API response structures
- Optimize based on real-world performance data
- Maintain backward compatibility with mock services for unimplemented endpoints
- Keep environment configurations updated
- **Documentation**: Update component documentation in relevant design docs
- **Documentation**: Create `docs/technical/frontend-backend-integration.md` when starting integration
- **Documentation**: Update ux.md if user flows change

### For test-writer-fixer Agent
- Create API integration tests
- Test Supabase Auth flows
- Validate data persistence
- Check error handling
- Ensure Romanian text support
- **Documentation**: Create `docs/technical/testing-strategy.md` for test approach
- **Documentation**: Update Implementation.md testing checklist
- **Documentation**: Document test scenarios in `docs/api/test-cases/`

### For devops-automator Agent
- Set up Railway deployment
- Configure GitHub Actions
- Implement health checks
- Set up monitoring
- Plan rollback strategies
- **Documentation**: Create `docs/deployment/railway-setup.md`
- **Documentation**: Create `docs/deployment/ci-cd-pipeline.md`
- **Documentation**: Document environment variables in `docs/deployment/configuration.md`

## 📋 Common Backend Tasks

### Creating API Endpoints
```csharp
// Minimal API pattern
app.MapGet("/api/issues", async (IIssueService service) =>
{
    var issues = await service.GetAllAsync();
    return Results.Ok(issues);
})
.RequireAuthorization()
.WithName("GetIssues")
.WithOpenApi();
```

### Supabase Auth Integration
```csharp
// JWT validation middleware
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Supabase:Url"];
        options.Audience = builder.Configuration["Supabase:AnonKey"];
    });
```

### Database Operations
```csharp
// Entity Framework Core pattern
public class IssueService : IIssueService
{
    private readonly CivicaDbContext _context;
    
    public async Task<Issue> CreateAsync(CreateIssueDto dto)
    {
        var issue = new Issue { /* mapping */ };
        _context.Issues.Add(issue);
        await _context.SaveChangesAsync();
        return issue;
    }
}
```

## 🚀 Quick Command Reference

```bash
# Backend architecture planning
"Use backend-architect agent to design the complete backend architecture based on Implementation.md"

# Backend implementation
"Use csharp-expert agent to implement the authentication endpoints with Supabase"

# API testing
"Use test-writer-fixer agent to create integration tests for all API endpoints"

# Performance testing
"Use performance-benchmarker agent to load test the API endpoints"

# Deployment setup
"Use devops-automator agent to set up Railway deployment pipeline"

# Frontend integration prep
"Use frontend-developer agent to prepare services for backend integration"
```

### API Endpoints (To Be Implemented)
- `POST /api/auth/register` - User registration
- `POST /api/auth/login` - User login
- `POST /api/auth/refresh` - Token refresh
- `GET /api/issues` - Get all issues
- `POST /api/issues` - Create new issue
- `GET /api/issues/{id}` - Get issue details
- `PUT /api/issues/{id}/email-sent` - Track email sent
- `GET /api/user/profile` - Get user profile
- `PUT /api/user/profile` - Update profile
- `GET /api/admin/pending-issues` - Admin: get pending
- `PUT /api/admin/issues/{id}/approve` - Admin: approve
- `PUT /api/admin/issues/{id}/reject` - Admin: reject

### Environment Variables (Railway)
```env
DATABASE_URL=postgresql://...
SUPABASE_URL=https://xxx.supabase.co
SUPABASE_ANON_KEY=xxx
SUPABASE_SERVICE_KEY=xxx
JWT_SECRET=xxx
```

## 📝 Documentation Requirements for ALL Agents

**EVERY agent MUST maintain documentation**:
1. **Before starting work**: Check if relevant documentation exists in `docs/`
2. **During development**: Track what documentation needs updating
3. **After completing work**: Update all affected documentation
4. **If documentation is missing**: Create new files in the appropriate `docs/` subfolder

**Documentation is NOT optional** - it's part of the definition of "done" for any task.

## 📊 Success Metrics

### Backend Development Checklist
- [ ] All API endpoints implemented
- [ ] Supabase Auth fully integrated
- [ ] Database schema matches design
- [ ] All tests passing (unit + integration)
- [ ] API documentation complete in `docs/api/`
- [ ] Performance benchmarks met
- [ ] Security best practices followed
- [ ] Railway deployment working
- [ ] Monitoring in place
- [ ] Error handling comprehensive
- [ ] **All documentation updated**

### Integration Checklist
- [ ] Frontend connects to real API
- [ ] Authentication flow works end-to-end
- [ ] Data persistence verified
- [ ] File uploads working
- [ ] Real-time features functional
- [ ] Performance acceptable
- [ ] No regressions from mock version
- [ ] **Integration guide created in docs/**

### Documentation Checklist
- [ ] Implementation.md reflects current state
- [ ] API endpoints documented
- [ ] Setup guides created
- [ ] Deployment procedures documented
- [ ] Architecture decisions recorded
- [ ] Test scenarios documented

## 🔄 Continuous Improvement

This CLAUDE.md file should be updated whenever:
- New patterns or conventions are established
- Backend development progresses
- Integration points are identified
- Common issues or solutions are discovered
- Deployment procedures change

**Remember**: This file is the single source of truth for all Claude agents working on Civica. Keep it accurate and up-to-date!