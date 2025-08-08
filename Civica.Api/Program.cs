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
using Civica.Api.Infrastructure.Constants;
using Civica.Api.Endpoints;

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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.AdminOnly, policy =>
        policy.RequireClaim(AuthorizationPolicies.Claims.Role, AuthorizationPolicies.Roles.Admin));
    
    options.AddPolicy(AuthorizationPolicies.UserOnly, policy =>
        policy.RequireClaim(AuthorizationPolicies.Claims.Role, AuthorizationPolicies.Roles.User));
});

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
app.MapGet("/api/health", () => Results.Ok(new 
{ 
    Status = "Healthy", 
    Timestamp = DateTime.UtcNow,
    Version = "1.0.0",
    Database = "pending",
    Supabase = "pending"
}))
.WithName("HealthCheck")
.WithOpenApi()
.WithSummary("Health check endpoint");

// Database migration on startup (Railway compatible)
try
{
    using (IServiceScope scope = app.Services.CreateScope())
    {
        CivicaDbContext context = scope.ServiceProvider.GetRequiredService<CivicaDbContext>();
        context.Database.Migrate();
        Log.Information("Database migration completed successfully");
    }
}
catch (Exception ex)
{
    Log.Error(ex, "Database migration failed");
    // Don't throw in production to allow app to start
    if (app.Environment.IsDevelopment())
        throw;
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");