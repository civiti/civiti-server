using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using FluentValidation;
using Civica.Api.Data;
using Civica.Api.Services.Interfaces;
using Civica.Api.Services;
using Civica.Api.Infrastructure.Middleware;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Configuration;
using Civica.Api.Endpoints;
using Swashbuckle.AspNetCore.Filters;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/civica-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(); // Add OpenAPI support for .NET 9

// Configure Swagger with comprehensive documentation
builder.Services.AddSwaggerGen(options => { options.ConfigureSwagger(builder.Configuration); });

// Add Swagger examples
builder.Services.AddSwaggerExamplesFromAssemblyOf<Program>();

// Database
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                       ?? builder.Configuration.GetConnectionString("PostgreSQL");

// Mask password in connection string for logging
var maskedConnectionString = connectionString;
if (!string.IsNullOrEmpty(connectionString))
{
    if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
    {
        // Handle URL format: postgres://user:password@host:port/database
        Regex regex = MyRegex();
        maskedConnectionString = regex.Replace(connectionString, "://$1:***@");
    }
    else
    {
        var passwordPart = connectionString.Split(';').FirstOrDefault(s => s.StartsWith("Password", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(passwordPart))
        {
            maskedConnectionString = connectionString.Replace(passwordPart, "Password=***");
        }
    }
}

// Convert Railway DATABASE_URL format to Npgsql connection string
if (connectionString?.StartsWith("postgres://") == true || connectionString?.StartsWith("postgresql://") == true)
{
    try
    {
        // Parse URL format: postgresql://user:password@host:port/database
        Uri uri = new(connectionString.Replace("postgres://", "postgresql://"));

        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : string.Empty;

        // Log parsed components (without password)
        Log.Information("Parsed DATABASE_URL - Host: {Host}, Port: {Port}, Database: {Database}, Username: {Username}",
            uri.Host, uri.Port, uri.AbsolutePath.TrimStart('/'), username);

        connectionString =
            $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;Timeout=30;Command Timeout=30;Connection Idle Lifetime=300;Maximum Pool Size=100;Include Error Detail=true";

        Log.Information("Converted Railway DATABASE_URL to Npgsql format successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to parse DATABASE_URL");
        throw new InvalidOperationException("Invalid DATABASE_URL format", ex);
    }
}

builder.Services.AddDbContext<CivicaDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory");
            npgsqlOptions.CommandTimeout(30);
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null);
        })
        .ConfigureWarnings(warnings =>
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
        .EnableDetailedErrors(builder.Environment.IsDevelopment()));

// Authentication with Supabase JWT
var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    supabaseUrl = builder.Configuration["Supabase:Url"];
}

var supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
if (string.IsNullOrWhiteSpace(supabaseAnonKey))
{
    supabaseAnonKey = builder.Configuration["Supabase:AnonKey"];
}

// Get JWT secret for token validation (different from anon key)
var jwtSecret = Environment.GetEnvironmentVariable("SUPABASE_JWT_SECRET");
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    jwtSecret = builder.Configuration["Supabase:JwtSecret"];
}

// Validate Supabase configuration early
if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    var errorMsg = $"Supabase URL not configured. Environment: {builder.Environment.EnvironmentName}. " +
                   "Please set SUPABASE_URL environment variable or configure Supabase:Url in appsettings.json/appsettings.Development.json. " +
                   "Check launchSettings.json for environment variables when debugging.";
    throw new InvalidOperationException(errorMsg);
}

if (string.IsNullOrWhiteSpace(supabaseAnonKey))
{
    var errorMsg = $"Supabase Anon Key not configured. Environment: {builder.Environment.EnvironmentName}. " +
                   "Please set SUPABASE_ANON_KEY environment variable or configure Supabase:AnonKey in appsettings.json/appsettings.Development.json. " +
                   "Check launchSettings.json for environment variables when debugging.";
    throw new InvalidOperationException(errorMsg);
}

// Define security policy based on environment
string environmentName = builder.Environment.EnvironmentName;

// For Supabase with JWT Keys, the JWT secret is now optional
// Supabase provides a JWKS endpoint for automatic key discovery
// The legacy JWT secret can still be provided for backward compatibility
if (!string.IsNullOrWhiteSpace(jwtSecret))
{
    Log.Information("Legacy JWT secret provided - will be used as fallback for {Environment}", environmentName);
}
else
{
    Log.Information("No JWT secret provided - will use Supabase JWKS endpoint for key discovery. " +
                    "This is the recommended approach for JWT Keys support.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Supabase JWTs can have different issuers depending on version
        // Check actual token with decode-jwt.html to verify
        var issuer = $"{supabaseUrl}/auth/v1";

        // For debugging - log the expected issuer
        Log.Information("Configuring JWT validation with issuer: {Issuer}", issuer);

        // Use Supabase's JWKS endpoint for automatic key discovery and rotation support
        // This supports both legacy HS256 and new ECC keys
        var jwksUri = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";

        // Configure to use JWKS for key discovery (supports key rotation)
        options.Authority = $"{supabaseUrl}/auth/v1";
        options.Audience = "authenticated";
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment(); // HTTPS required except in dev
        options.SaveToken = true;

        // Try to use JWKS endpoint first for automatic key discovery
        options.MetadataAddress = jwksUri;

        // Token validation parameters
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, // Will use keys from JWKS
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidIssuer = issuer,
            ValidAudience = "authenticated"
        };

        // If JWT secret is provided (legacy support), add it as a fallback
        if (!string.IsNullOrWhiteSpace(jwtSecret))
        {
            options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret));
            Log.Information("JWT validation configured with legacy secret as fallback for {Environment}", environmentName);
        }

        Log.Information("JWT validation configured with JWKS endpoint: {JwksUri}", jwksUri);

        // Log JWT validation events for debugging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Error("Authentication failed: {Error}", context.Exception?.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Log.Information("Token validated for user: {UserId}",
                    context.Principal?.FindFirst("sub")?.Value);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Log.Warning("JWT Challenge: {Error} - {ErrorDescription}",
                    context.Error, context.ErrorDescription);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.AdminOnly, policy =>
        policy.RequireClaim(AuthorizationPolicies.Claims.Role, AuthorizationPolicies.Roles.Admin))
    .AddPolicy(AuthorizationPolicies.UserOnly, policy =>
        policy.RequireClaim(AuthorizationPolicies.Claims.Role, AuthorizationPolicies.Roles.User));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("CivicaPolicy", policy =>
    {
        // Get allowed origins from configuration or environment variable
        // Fix: Check for null or whitespace before splitting to avoid empty array blocking fallback
        var envOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
        string[]? corsOrigins = null;

        // Only use environment variable if it has actual content
        if (!string.IsNullOrWhiteSpace(envOrigins))
        {
            corsOrigins = envOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // If splitting resulted in empty array, set to null to trigger fallback
            if (corsOrigins.Length == 0)
            {
                corsOrigins = null;
            }
        }

        // Fallback to configuration if environment variable is not usable
        corsOrigins ??= builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                        ?? ["http://localhost:4200"];

        Log.Information("CORS configured with allowed origins: {Origins}", string.Join(", ", corsOrigins));

        policy.WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Register Supabase configuration
// Note: Validation already done above before JWT configuration
builder.Services.AddSingleton(new SupabaseConfiguration
{
    Url = supabaseUrl,
    AnonKey = supabaseAnonKey
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

// HttpClient for development endpoints
builder.Services.AddHttpClient();

WebApplication app = builder.Build();

// Configure pipeline~~~
// Enable Swagger in both Development and Production for Railway deployment
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Civica API v1");
    options.RoutePrefix = "swagger";
});

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Static files for Swagger UI custom styling (must be before UseSwagger)
app.UseStaticFiles();

// Enable Swagger in both Development and Production for Railway deployment
app.UseSwagger();

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Civica API v1");
    options.RoutePrefix = "swagger";

    // Configure UI
    options.DocumentTitle = "Civica API Documentation";
    options.DefaultModelsExpandDepth(2);
    options.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Model);
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    options.EnableDeepLinking();
    options.DisplayRequestDuration();
    options.EnableTryItOutByDefault();

    // Add custom CSS for better styling
    options.InjectStylesheet("/swagger-ui/custom.css");
});

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
app.MapDevAuthEndpoints(); // Development-only endpoints for testing

// Root endpoint redirects to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

// Debug endpoint to check swagger generation
app.MapGet("/swagger-debug", async (HttpContext context) =>
    {
        var swaggerUrl = $"{context.Request.Scheme}://{context.Request.Host}/swagger/v1/swagger.json";
        return Results.Ok(new
        {
            message = "If Swagger is working, the JSON should be available at the URL below",
            swaggerJsonUrl = swaggerUrl,
            hint = "Navigate directly to this URL to see if the JSON is generated correctly"
        });
    })
    .ExcludeFromDescription();

app.MapGet("/api/health", async (CivicaDbContext context, ISupabaseService supabaseService) =>
    {
        var health = new
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Version = "1.0.0",
            Database = "unknown",
            DatabaseError = (string?)null,
            Supabase = "unknown",
            Environment = app.Environment.EnvironmentName
        };

        try
        {
            // Test database connectivity with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await context.Database.CanConnectAsync(cts.Token);
            health = health with { Database = "connected" };
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Database health check timed out after 5 seconds");
            health = health with { Database = "timeout", DatabaseError = "Connection timeout (5s)" };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database health check failed");
            health = health with { Database = "disconnected", DatabaseError = ex.Message };
        }

        try
        {
            // Test Supabase connectivity
            var supabaseHealthy = await supabaseService.CheckHealthAsync();
            health = health with { Supabase = supabaseHealthy ? "connected" : "disconnected" };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Supabase health check failed");
            health = health with { Supabase = "disconnected" };
        }

        var overallStatus = health.Database == "connected" ? "Healthy" : "Degraded";
        return Results.Ok(health with { Status = overallStatus });
    })
    .WithName("HealthCheck")
    .WithTags("Health")
    .WithOpenApi()
    .WithSummary("Health check endpoint with connectivity tests")
    .WithDescription(
        "Performs health checks on critical dependencies including PostgreSQL database and Supabase authentication service. Returns detailed connectivity status for each component.")
    .Produces(200);

// Database migration on startup (Railway compatible with retry logic)
var skipMigration = Environment.GetEnvironmentVariable("SKIP_DB_MIGRATION") == "true";

if (!skipMigration)
{
    Log.Information("Attempting database migration...");

    const int maxRetries = 5;
    const int delayMs = 5000;
    bool migrationSuccess = false;

    for (int retry = 1; retry <= maxRetries; retry++)
    {
        try
        {
            using IServiceScope scope = app.Services.CreateScope();
            CivicaDbContext context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();

            // Test connection first with shorter timeout
            Log.Information($"Testing database connection (attempt {retry}/{maxRetries})...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool canConnect = await context.Database.CanConnectAsync(cts.Token);

            if (!canConnect)
            {
                Log.Warning($"Cannot connect to database on attempt {retry}");
                if (retry < maxRetries)
                {
                    await Task.Delay(delayMs * retry); // Exponential backoff
                    continue;
                }
                else
                {
                    // Final retry failed - don't attempt migration
                    Log.Error("Database connection failed after all retries - skipping migration");
                    break;
                }
            }

            // Only attempt migration if we can connect
            Log.Information("Database connection successful - executing migration...");
            await context.Database.MigrateAsync();
            Log.Information("Database migration completed successfully");
            migrationSuccess = true;
            break;
        }
        catch (OperationCanceledException)
        {
            Log.Warning($"Database connection timed out on attempt {retry}");
            if (retry < maxRetries)
            {
                Log.Information($"Waiting {delayMs * retry}ms before retry...");
                await Task.Delay(delayMs * retry);
                continue; // Explicitly continue to next retry
            }
            else
            {
                Log.Error("Database connection timed out after all retries");
                break; // Exit the retry loop
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Database migration attempt {retry} failed");

            if (retry < maxRetries)
            {
                Log.Information($"Waiting {delayMs * retry}ms before retry...");
                await Task.Delay(delayMs * retry); // Exponential backoff
                continue; // Explicitly continue to next retry
            }
            else
            {
                Log.Error("All database migration attempts failed");
                // Don't throw in production to allow app to start
                if (app.Environment.IsDevelopment())
                    throw;
                break; // Exit the retry loop
            }
        }
    }

    if (!migrationSuccess)
    {
        Log.Warning("Application starting without successful database migration - database operations may fail");
    }
}
else
{
    Log.Information("Skipping database migration due to SKIP_DB_MIGRATION=true");
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Log.Information("Starting application on port {Port}", port);
await app.RunAsync($"http://0.0.0.0:{port}");

partial class Program
{
    [GeneratedRegex(@"://([^:]+):([^@]+)@")]
    private static partial Regex MyRegex();
}