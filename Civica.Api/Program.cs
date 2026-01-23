using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Serilog;
using FluentValidation;
using Civica.Api.Data;
using Civica.Api.Services.Interfaces;
using Civica.Api.Services;
using Civica.Api.Infrastructure.Middleware;
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Infrastructure.Configuration;
using Civica.Api.Infrastructure.Extensions;
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

// Configure JSON serialization to handle enums as strings (case-insensitive)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

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

// Log the masked connection string for debugging (without exposing password)
if (!string.IsNullOrEmpty(maskedConnectionString))
{
    Log.Information("Database connection configured: {MaskedConnectionString}", maskedConnectionString);
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

// Authentication with Supabase JWT and JWKS
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
var environmentName = builder.Environment.EnvironmentName;

// Configure JWT validation options for JWKS support
JwtValidationOptions jwtValidationOptions = new JwtValidationOptions
{
    JwksUrl = $"{supabaseUrl}/auth/v1/.well-known/jwks.json",
    ValidIssuer = $"{supabaseUrl}/auth/v1",
    ValidAudience = "authenticated",
    JwksCacheTtlMs = 60 * 60 * 1000, // 1 hour cache
    ClockSkew = TimeSpan.Zero,
    RequireHttpsMetadata = !builder.Environment.IsDevelopment()
};

// Register JWT validation options
builder.Services.Configure<JwtValidationOptions>(options =>
{
    options.JwksUrl = jwtValidationOptions.JwksUrl;
    options.ValidIssuer = jwtValidationOptions.ValidIssuer;
    options.ValidAudience = jwtValidationOptions.ValidAudience;
    options.JwksCacheTtlMs = jwtValidationOptions.JwksCacheTtlMs;
    options.ClockSkew = jwtValidationOptions.ClockSkew;
    options.RequireHttpsMetadata = jwtValidationOptions.RequireHttpsMetadata;
});

Log.Information("Using Supabase JWKS endpoint for key discovery: {JwksUrl}", jwtValidationOptions.JwksUrl);

// Register JWKS Manager service and dependencies
builder.Services.AddSingleton<IJwksManager, JwksManager>();
builder.Services.AddMemoryCache();
builder.Services.AddHostedService<JwksBackgroundService>();

// Register demo data seeder (Development only)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<DemoDataSeeder>();
}

// Add JWT Bearer authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Basic JWT Bearer configuration
        options.RequireHttpsMetadata = jwtValidationOptions.RequireHttpsMetadata;
        options.SaveToken = true;

        // Disable automatic claim type mapping
        // This keeps JWT claims as-is (e.g., "sub" stays "sub", not mapped to NameIdentifier)
        // This is the recommended approach for working with external identity providers like Supabase
        options.MapInboundClaims = false;

        // Token validation parameters
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtValidationOptions.ValidIssuer,
            ValidateAudience = true,
            ValidAudience = jwtValidationOptions.ValidAudience,
            ValidateLifetime = true,
            ClockSkew = jwtValidationOptions.ClockSkew,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true
        };

        // Note: IssuerSigningKeyResolver is configured via JwtBearerPostConfigureOptions
        // This ensures proper dependency injection and synchronous cache access

        // Configure events for authentication logging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Warning("JWT authentication failed: {Error}", context.Exception?.Message);
                return Task.CompletedTask;
            }
        };

        Log.Information("JWT Bearer authentication configured - Issuer: {Issuer}, JWKS: {JwksUrl}",
            jwtValidationOptions.ValidIssuer, jwtValidationOptions.JwksUrl);
    });

// Configure JWT Bearer options with proper dependency injection
builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, Civica.Api.Infrastructure.Configuration.JwtBearerPostConfigureOptions>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.AdminOnly, policy =>
        policy.RequireAssertion(context => context.User.IsAdmin()))
    .AddPolicy(AuthorizationPolicies.UserOnly, policy =>
        policy.RequireAuthenticatedUser());

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

// Claude AI Configuration
var claudeConfig = new ClaudeConfiguration
{
    ApiKey = GetEnvOrConfig("CLAUDE_API_KEY", "Claude:ApiKey") ?? string.Empty,
    Model = GetEnvOrConfig("CLAUDE_MODEL", "Claude:Model") ?? ClaudeConfiguration.DefaultModel,
    MaxTokens = GetEnvOrConfigInt("CLAUDE_MAX_TOKENS", "Claude:MaxTokens", ClaudeConfiguration.DefaultMaxTokens),
    TimeoutSeconds = GetEnvOrConfigInt("CLAUDE_TIMEOUT_SECONDS", "Claude:TimeoutSeconds", ClaudeConfiguration.DefaultTimeoutSeconds),
    RateLimitPerMinute = GetEnvOrConfigInt("CLAUDE_RATE_LIMIT_PER_MINUTE", "Claude:RateLimitPerMinute", ClaudeConfiguration.DefaultRateLimitPerMinute)
};
builder.Services.AddSingleton(claudeConfig);

// Configure rate limiter for Claude AI requests using sliding window algorithm
builder.Services.AddSingleton<PartitionedRateLimiter<Guid>>(sp =>
{
    var config = sp.GetRequiredService<ClaudeConfiguration>();
    return PartitionedRateLimiter.Create<Guid, Guid>(userId =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: userId,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = config.RateLimitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6, // 10-second segments for smoother rate limiting
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

if (claudeConfig.IsConfigured)
{
    Log.Information("Claude AI configured with model: {Model}", claudeConfig.Model);
}
else
{
    Log.Warning("Claude API key is not configured. AI text enhancement will return original text.");
}

string? GetEnvOrConfig(string envVar, string configKey)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    return !string.IsNullOrWhiteSpace(value) ? value : builder.Configuration[configKey];
}

int GetEnvOrConfigInt(string envVar, string configKey, int defaultValue)
{
    var envValue = Environment.GetEnvironmentVariable(envVar);
    if (int.TryParse(envValue, out var result))
    {
        return result;
    }
    return builder.Configuration.GetValue(configKey, defaultValue);
}

// Custom services
builder.Services.AddScoped<ISupabaseService, SupabaseService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IIssueService, IssueService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IGamificationService, GamificationService>();
builder.Services.AddScoped<IAuthorityService, AuthorityService>();
builder.Services.AddScoped<IClaudeEnhancementService, ClaudeEnhancementService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<ICommentService, CommentService>();

// Validators
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// HttpClient for development endpoints
builder.Services.AddHttpClient();

WebApplication app = builder.Build();

// Configure forwarded headers for reverse proxy (Railway, etc.)
// This must be first in the pipeline to correctly set RemoteIpAddress
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1 // Only trust the first proxy hop to prevent spoofing
};
// Clear default known networks/proxies to allow any proxy (needed for cloud deployments)
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Configure pipeline
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
app.MapAuthorityEndpoints();
app.MapUtilityEndpoints(); // Utility endpoints (categories, etc.)
app.MapJwksEndpoints(); // JWKS management and monitoring endpoints
app.MapDevAuthEndpoints(); // Development-only endpoints for testing
app.MapActivityEndpoints(); // Activity feed endpoints
app.MapCommentEndpoints(); // Comment endpoints

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
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
    var migrationSuccess = false;

    for (var retry = 1; retry <= maxRetries; retry++)
    {
        try
        {
            using IServiceScope scope = app.Services.CreateScope();
            CivicaDbContext context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();

            // Test connection first with shorter timeout
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var canConnect = await context.Database.CanConnectAsync(cts.Token);

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

            Log.Error("Database connection timed out after all retries");
            break; // Exit the retry loop
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Database migration attempt {retry} failed");

            if (retry < maxRetries)
            {
                Log.Information($"Waiting {delayMs * retry}ms before retry...");
                await Task.Delay(delayMs * retry); // Exponential backoff
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

// Pre-populate JWKS cache before starting the application
// This ensures keys are available for the synchronous IssuerSigningKeyResolver
try
{
    IJwksManager jwksManager = app.Services.GetRequiredService<IJwksManager>();
    Log.Information("Pre-populating JWKS cache before application start");

    JsonWebKeySet jwks = await jwksManager.GetJwksAsync();
    Log.Information("JWKS cache populated successfully with {KeyCount} keys", jwks.Keys.Count);

    // Log available key IDs for debugging
    var kids = string.Join(", ", jwks.Keys.Select(k => k.Kid ?? "null"));
    Log.Debug("Available key IDs: {Kids}", kids);
}
catch (Exception ex)
{
    Log.Error(ex, "Failed to pre-populate JWKS cache - JWT validation may fail initially");
    // Continue running - the background service will keep trying
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Log.Information("Starting application on port {Port}", port);
await app.RunAsync($"http://0.0.0.0:{port}");

partial class Program
{
    [GeneratedRegex(@"://([^:]+):([^@]+)@")]
    private static partial Regex MyRegex();
}