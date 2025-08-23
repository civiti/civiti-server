using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        connectionString =
            $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";

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
            npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory"))
        .ConfigureWarnings(warnings =>
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = supabaseUrl;
        options.Audience = "authenticated";
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        options.SaveToken = true;
        options.TokenValidationParameters = new()
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
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
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

WebApplication app = builder.Build();

// Configure pipeline
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
            Supabase = "unknown"
        };

        try
        {
            // Test database connectivity
            await context.Database.CanConnectAsync();
            health = health with { Database = "connected" };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database health check failed");
            health = health with { Database = "disconnected" };
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

        var overallStatus = health.Database == "connected" ? "Healthy" : "Unhealthy";
        return Results.Ok(health with { Status = overallStatus });
    })
    .WithName("HealthCheck")
    .WithTags("Health")
    .WithOpenApi()
    .WithSummary("Health check endpoint with connectivity tests")
    .WithDescription(
        "Performs health checks on critical dependencies including PostgreSQL database and Supabase authentication service. Returns detailed connectivity status for each component.")
    .Produces(200);

// Database migration on startup (Railway compatible)
Log.Information("Attempting database migration...");

try
{
    using IServiceScope scope = app.Services.CreateScope();
    CivicaDbContext context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();

    // EF Core Migrate() will create the database if it doesn't exist
    await context.Database.MigrateAsync();
    Log.Information("Database migration completed successfully");
}
catch (Exception ex)
{
    Log.Error(ex, "Database migration failed");
    // Don't throw in production to allow app to start
    if (app.Environment.IsDevelopment())
        throw;
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Log.Information("Starting application on port {Port}", port);
await app.RunAsync($"http://0.0.0.0:{port}");

partial class Program
{
    [GeneratedRegex(@"://([^:]+):([^@]+)@")]
    private static partial Regex MyRegex();
}