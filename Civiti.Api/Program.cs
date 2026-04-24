using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Anthropic.SDK;
using Civiti.Infrastructure.Data;
using Civiti.Api.Endpoints;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Api.Infrastructure.Constants;
using Civiti.Domain.Constants;
using Civiti.Api.Infrastructure.Extensions;
using Civiti.Api.Infrastructure.Middleware;
using Civiti.Application.Email.Models;
using Civiti.Application.Notifications;
using Civiti.Application.Push.Models;
using Civiti.Infrastructure.Configuration;
using Civiti.Infrastructure.Services;
using Civiti.Infrastructure.Services.AdminNotify;
using Civiti.Infrastructure.Services.Claude;
using Civiti.Infrastructure.Services.Email;
using Civiti.Infrastructure.Services.Jwks;
using Civiti.Infrastructure.Services.Moderation;
using Civiti.Infrastructure.Services.Poster;
using Civiti.Infrastructure.Services.Push;
using Civiti.Infrastructure.Services.Supabase;
using Civiti.Application.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Resend;
using Serilog;
using Swashbuckle.AspNetCore.Filters;
using JwtBearerPostConfigureOptions = Civiti.Api.Infrastructure.Configuration.JwtBearerPostConfigureOptions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/civiti-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddEndpointsApiExplorer();
// Configure JSON serialization to handle enums as strings (case-insensitive)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
});

// Configure Swagger with comprehensive documentation
builder.Services.AddSwaggerGen(options => { options.ConfigureSwagger(builder.Configuration); });

// Add Swagger examples
builder.Services.AddSwaggerExamplesFromAssemblyOf<Civiti.Api.Program>();

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
        Regex regex = Civiti.Api.Program.MyRegex();
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

        // Include Error Detail embeds PostgreSQL-internal error strings (table/column names,
        // constraint details, partial row data) into Npgsql exception messages. Useful in
        // development for debugging; an information-disclosure risk in production where those
        // messages can end up in exception responses or forwarded logs.
        var includeErrorDetail = builder.Environment.IsDevelopment() ? ";Include Error Detail=true" : string.Empty;

        connectionString =
            $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;Timeout=30;Command Timeout=30;Connection Idle Lifetime=300;Maximum Pool Size=100{includeErrorDetail}";

        Log.Information("Converted Railway DATABASE_URL to Npgsql format successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to parse DATABASE_URL");
        throw new InvalidOperationException("Invalid DATABASE_URL format", ex);
    }
}

// Configure DbContext with PostgreSQL
// Note: Enums are stored as integers (EF Core default) for simpler migration handling
builder.Services.AddDbContext<CivitiDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            // CivitiDbContext lives in Civiti.Infrastructure; the matching migrations live
            // alongside it. MigrationsAssembly() pins EF to that assembly so `dotnet ef`
            // (invoked with Civiti.Api as the startup project) finds them.
            npgsqlOptions.MigrationsAssembly(typeof(CivitiDbContext).Assembly.GetName().Name);
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

var supabasePublishableKey = Environment.GetEnvironmentVariable("SUPABASE_PUBLISHABLE_KEY")
    ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY"); // legacy fallback
if (string.IsNullOrWhiteSpace(supabasePublishableKey))
{
    supabasePublishableKey = builder.Configuration["Supabase:PublishableKey"];
}

// Validate Supabase configuration early
if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    var errorMsg = $"Supabase URL not configured. Environment: {builder.Environment.EnvironmentName}. " +
                   "Please set SUPABASE_URL environment variable or configure Supabase:Url in appsettings.json/appsettings.Development.json. " +
                   "Check launchSettings.json for environment variables when debugging.";
    throw new InvalidOperationException(errorMsg);
}

if (string.IsNullOrWhiteSpace(supabasePublishableKey))
{
    var errorMsg = $"Supabase Publishable Key not configured. Environment: {builder.Environment.EnvironmentName}. " +
                   "Please set SUPABASE_PUBLISHABLE_KEY environment variable or configure Supabase:PublishableKey in appsettings.json/appsettings.Development.json. " +
                   "Check launchSettings.json for environment variables when debugging.";
    throw new InvalidOperationException(errorMsg);
}

// Configure JWT validation options for JWKS support
JwtValidationOptions jwtValidationOptions = new()
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

// Register static data seeder (badges, achievements)
builder.Services.AddHostedService<StaticDataSeeder>();

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
builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, JwtBearerPostConfigureOptions>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.AdminOnly, policy =>
        policy.RequireAssertion(context => context.User.IsAdmin()))
    .AddPolicy(AuthorizationPolicies.UserOnly, policy =>
        policy.RequireAuthenticatedUser());

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("CivitiPolicy", policy =>
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

// Supabase Service Role Key (optional - needed for admin user deletion)
var supabaseServiceRoleKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
if (string.IsNullOrWhiteSpace(supabaseServiceRoleKey))
{
    supabaseServiceRoleKey = builder.Configuration["Supabase:ServiceRoleKey"];
}

if (string.IsNullOrWhiteSpace(supabaseServiceRoleKey))
{
    Log.Warning("SUPABASE_SERVICE_ROLE_KEY not configured. Account deletion will soft-delete locally but cannot remove the Supabase Auth account.");
}

// Register Supabase configuration
// Note: Validation already done above before JWT configuration
builder.Services.AddSingleton(new SupabaseConfiguration
{
    Url = supabaseUrl,
    PublishableKey = supabasePublishableKey,
    ServiceRoleKey = supabaseServiceRoleKey ?? string.Empty
});

// Claude AI Configuration
ClaudeConfiguration claudeConfig = new()
{
    ApiKey = GetEnvOrConfig("CLAUDE_API_KEY", "Claude:ApiKey") ?? string.Empty,
    Model = GetEnvOrConfig("CLAUDE_MODEL", "Claude:Model") ?? ClaudeConfiguration.DefaultModel,
    MaxTokens = GetEnvOrConfigInt("CLAUDE_MAX_TOKENS", "Claude:MaxTokens", ClaudeConfiguration.DefaultMaxTokens),
    TimeoutSeconds = GetEnvOrConfigInt("CLAUDE_TIMEOUT_SECONDS", "Claude:TimeoutSeconds", ClaudeConfiguration.DefaultTimeoutSeconds),
    RateLimitPerMinute = GetEnvOrConfigInt("CLAUDE_RATE_LIMIT_PER_MINUTE", "Claude:RateLimitPerMinute", ClaudeConfiguration.DefaultRateLimitPerMinute)
};
builder.Services.AddSingleton(claudeConfig);

// AnthropicClient wraps a SocketsHttpHandler-backed HttpClient. Register as a
// singleton so the connection pool is shared across all ClaudeEnhancementService
// calls — constructing a new client per request exhausts sockets under load.
// When Claude is not configured, the client is still registered but never exercised:
// ClaudeEnhancementService short-circuits on ClaudeConfiguration.IsConfigured before
// calling it.
builder.Services.AddSingleton<AnthropicClient>(_ => new AnthropicClient(claudeConfig.ApiKey));

// Configure rate limiter for Claude AI requests using sliding window algorithm
builder.Services.AddSingleton<PartitionedRateLimiter<Guid>>(sp =>
{
    ClaudeConfiguration config = sp.GetRequiredService<ClaudeConfiguration>();
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

// OpenAI Configuration (for content moderation)
OpenAIConfiguration openAIConfig = new()
{
    ApiKey = GetEnvOrConfig("OPENAI_API_KEY", "OpenAI:ApiKey") ?? string.Empty,
    ModerationModel = GetEnvOrConfig("OPENAI_MODERATION_MODEL", "OpenAI:ModerationModel") ?? OpenAIConfiguration.DefaultModerationModel,
    TimeoutSeconds = GetEnvOrConfigInt("OPENAI_TIMEOUT_SECONDS", "OpenAI:TimeoutSeconds", OpenAIConfiguration.DefaultTimeoutSeconds)
};
builder.Services.AddSingleton(openAIConfig);

if (openAIConfig.IsConfigured)
{
    Log.Information("OpenAI content moderation configured with model: {Model}", openAIConfig.ModerationModel);
}
else
{
    Log.Warning("OpenAI API key is not configured. Content moderation will be skipped.");
}

// Poster Configuration
PosterConfiguration posterConfig = new()
{
    FrontendBaseUrl = GetEnvOrConfig("POSTER_FRONTEND_BASE_URL", "Poster:FrontendBaseUrl") ?? "https://civiti.ro",
    QrSizePixels = GetEnvOrConfigInt("POSTER_QR_SIZE_PIXELS", "Poster:QrSizePixels", 300),
    CacheDurationMinutes = GetEnvOrConfigInt("POSTER_CACHE_DURATION_MINUTES", "Poster:CacheDurationMinutes", 15)
};
builder.Services.AddSingleton(posterConfig);
Log.Information("Poster generation configured with frontend URL: {FrontendBaseUrl}", posterConfig.FrontendBaseUrl);

// Resend Email Configuration
ResendConfiguration resendConfig = new()
{
    ApiKey = GetEnvOrConfig("RESEND_API_KEY", "Resend:ApiKey") ?? string.Empty,
    FromEmail = GetEnvOrConfig("RESEND_FROM_EMAIL", "Resend:FromEmail") ?? "Civiti <noreply@civiti.ro>",
    FrontendBaseUrl = GetEnvOrConfig("RESEND_FRONTEND_BASE_URL", "Resend:FrontendBaseUrl") ?? posterConfig.FrontendBaseUrl,
    DebounceMinutes = GetEnvOrConfigInt("RESEND_DEBOUNCE_MINUTES", "Resend:DebounceMinutes", 5)
};
builder.Services.AddSingleton(resendConfig);

if (resendConfig.IsConfigured)
{
    Log.Information("Resend email configured with from: {FromEmail}", resendConfig.FromEmail);
}
else
{
    Log.Warning("Resend API key is not configured. Email notifications will be skipped.");
}

// Email notification channel (bounded).
//   FullMode = Wait so that TryWrite returns false on overflow and callers can log/react.
//   DropWrite would silently succeed-and-drop, making the drop-logging throughout the
//   codebase dead code. Nobody calls WriteAsync/WaitToWriteAsync on this channel, so
//   "Wait" here never actually blocks — it just changes TryWrite's overflow return to false.
Channel<EmailNotification> emailChannel = Channel.CreateBounded<EmailNotification>(
    new BoundedChannelOptions(resendConfig.ChannelCapacity) { FullMode = BoundedChannelFullMode.Wait });
builder.Services.AddSingleton(emailChannel.Reader);
builder.Services.AddSingleton(emailChannel.Writer);

// Resend SDK (HttpClient + options pattern)
builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
{
    o.ApiToken = resendConfig.ApiKey;
});
builder.Services.AddTransient<IResend, ResendClient>();

// Email services
builder.Services.AddTransient<IEmailSenderService, EmailSenderService>();
builder.Services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddHostedService<EmailSenderBackgroundService>();

// Admin-on-new-issue notifications (Supabase-sourced admin list → email fanout)
AdminNotifyConfiguration adminNotifyConfig = new()
{
    Enabled = GetEnvOrConfigBool("ADMIN_NOTIFY_ENABLED", "AdminNotify:Enabled", !builder.Environment.IsDevelopment()),
    ChannelCapacity = GetEnvOrConfigInt("ADMIN_NOTIFY_CHANNEL_CAPACITY", "AdminNotify:ChannelCapacity", 1_000),
    AdminListCacheSeconds = GetEnvOrConfigInt("ADMIN_NOTIFY_CACHE_SECONDS", "AdminNotify:AdminListCacheSeconds", 60),
    MaxSupabaseRetries = GetEnvOrConfigInt("ADMIN_NOTIFY_MAX_RETRIES", "AdminNotify:MaxSupabaseRetries", 3),
    SupabaseTimeoutSeconds = GetEnvOrConfigInt("ADMIN_NOTIFY_SUPABASE_TIMEOUT_SECONDS", "AdminNotify:SupabaseTimeoutSeconds", 10),
    SupabasePageSize = GetEnvOrConfigInt("ADMIN_NOTIFY_SUPABASE_PAGE_SIZE", "AdminNotify:SupabasePageSize", 200),
    MaxSupabasePages = GetEnvOrConfigInt("ADMIN_NOTIFY_MAX_PAGES", "AdminNotify:MaxSupabasePages", 50)
};
if (adminNotifyConfig.ChannelCapacity <= 0)
    throw new InvalidOperationException($"AdminNotify:ChannelCapacity must be positive (got {adminNotifyConfig.ChannelCapacity}).");
if (adminNotifyConfig.SupabasePageSize is <= 0 or > 1_000)
    throw new InvalidOperationException($"AdminNotify:SupabasePageSize must be in (0, 1000] (got {adminNotifyConfig.SupabasePageSize}).");

builder.Services.AddSingleton(adminNotifyConfig);

// Wait mode: TryWrite returns false on overflow so the producer (AdminNotifier) can
// actually log and react. DropWrite would silently succeed-and-drop. Nobody uses
// WriteAsync on this channel, so Wait here never blocks.
Channel<AdminNotifyRequest> adminNotifyChannel = Channel.CreateBounded<AdminNotifyRequest>(
    new BoundedChannelOptions(adminNotifyConfig.ChannelCapacity) { FullMode = BoundedChannelFullMode.Wait });
builder.Services.AddSingleton(adminNotifyChannel.Reader);
builder.Services.AddSingleton(adminNotifyChannel.Writer);

builder.Services.AddHttpClient(SupabaseAdminClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(adminNotifyConfig.SupabaseTimeoutSeconds);
});
builder.Services.AddSingleton<ISupabaseAdminClient, SupabaseAdminClient>();
builder.Services.AddSingleton<IAdminNotifier, AdminNotifier>();

if (adminNotifyConfig.Enabled)
{
    builder.Services.AddHostedService<AdminNotifyBackgroundService>();
    Log.Information("Admin-on-new-issue notifications enabled (cache {CacheSec}s, channel capacity {Capacity}).",
        adminNotifyConfig.AdminListCacheSeconds, adminNotifyConfig.ChannelCapacity);
}
else
{
    Log.Information("Admin-on-new-issue notifications disabled via ADMIN_NOTIFY_ENABLED=false.");
}

// Expo Push Notification Configuration
ExpoPushConfiguration expoPushConfig = new()
{
    AccessToken = GetEnvOrConfig("EXPO_PUSH_ACCESS_TOKEN", "ExpoPush:AccessToken"),
    ChannelCapacity = GetEnvOrConfigInt("EXPO_PUSH_CHANNEL_CAPACITY", "ExpoPush:ChannelCapacity", 10_000),
    BatchSize = GetEnvOrConfigInt("EXPO_PUSH_BATCH_SIZE", "ExpoPush:BatchSize", 100)
};
if (expoPushConfig.BatchSize <= 0 || expoPushConfig.BatchSize > 100)
    throw new InvalidOperationException($"ExpoPush:BatchSize must be between 1 and 100 inclusive (got {expoPushConfig.BatchSize}).");
if (expoPushConfig.ChannelCapacity <= 0)
    throw new InvalidOperationException($"ExpoPush:ChannelCapacity must be a positive integer (got {expoPushConfig.ChannelCapacity}).");

builder.Services.AddSingleton(expoPushConfig);

if (!string.IsNullOrWhiteSpace(expoPushConfig.AccessToken))
    Log.Information("Expo push configured with access token.");
else
    Log.Warning("Expo push access token not configured — operating in unauthenticated mode (low-volume only).");

// Push notification channel (bounded, drop-write if full)
Channel<PushNotificationMessage> pushChannel = Channel.CreateBounded<PushNotificationMessage>(
    new BoundedChannelOptions(expoPushConfig.ChannelCapacity) { FullMode = BoundedChannelFullMode.DropWrite });
builder.Services.AddSingleton(pushChannel.Reader);
builder.Services.AddSingleton(pushChannel.Writer);

builder.Services.AddHttpClient("ExpoPush", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHostedService<PushNotificationSenderBackgroundService>();

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
builder.Services.AddScoped<IContentModerationService, OpenAIModerationService>();
builder.Services.AddScoped<IPosterService, PosterService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPushTokenService, PushTokenService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IBlockService, BlockService>();

// Built-in validation (DataAnnotations + IValidatableObject)
builder.Services.AddValidation();


// HttpClient for development endpoints
builder.Services.AddHttpClient();

WebApplication app = builder.Build();

// Proxy trust — resolve the real client IP from X-Forwarded-For without trusting headers from
// arbitrary upstreams. See Civiti.Mcp/Program.cs for the observed Railway chain (verified via
// the PR #91 diagnostic, including a spoofed-XFF probe) and the derivation. Both hosts sit
// behind the same edge and need identical trust rules. When duplication warrants, this moves
// to Civiti.Web (architecture.md §3).
const int RailwayAppendedHopCount = 2; // LB hop + internal hop; re-verify if Railway's edge changes.
IPNetwork[] trustedProxyRanges =
[
    IPNetwork.Parse("100.64.0.0/10"), // Railway internal edge (RFC 6598 CGNAT)
    IPNetwork.Parse("127.0.0.0/8"),   // IPv4 loopback (local dev)
    IPNetwork.Parse("::1/128")        // IPv6 loopback
];

app.Use(async (context, next) =>
{
    var upstream = context.Connection.RemoteIpAddress;
    // Kestrel's dual-stack sockets hand loopback addresses to us as ::ffff:127.0.0.1; unwrap
    // before range matching so local dev still goes through the trust path.
    if (upstream is { IsIPv4MappedToIPv6: true })
    {
        upstream = upstream.MapToIPv4();
    }

    if (upstream is not null && trustedProxyRanges.Any(n => n.Contains(upstream)))
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues) && xffValues.Count > 0)
        {
            // Railway preserves client-supplied XFF entries and appends its own two hops, so the
            // real client IP is at (len - RailwayAppendedHopCount). Everything left of that is
            // attacker-supplied and discarded. StringValues.ToString() joins multi-header-line
            // values with commas (RFC 7230 treats them as one logical list) — parsing only the
            // first line would let the hop-count index land on an attacker-controlled entry.
            var entries = xffValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (entries.Length >= RailwayAppendedHopCount)
            {
                var clientEntry = entries[entries.Length - RailwayAppendedHopCount];
                if (IPAddress.TryParse(clientEntry, out var clientIp))
                {
                    context.Connection.RemoteIpAddress = clientIp;
                }
            }
        }

        if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var xfpValues) && xfpValues.Count > 0)
        {
            // Every Railway hop stamps X-Forwarded-Proto; the rightmost entry (across all
            // header lines) is Railway's authoritative view, anything to its left could be
            // client-supplied.
            var protoEntries = xfpValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (protoEntries.Length > 0)
            {
                var scheme = protoEntries[^1];
                if (scheme is "http" or "https")
                {
                    context.Request.Scheme = scheme;
                }
            }
        }
    }

    await next(context);
});

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
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Civiti API v1");
    options.RoutePrefix = "swagger";

    // Configure UI
    options.DocumentTitle = "Civiti API Documentation";
    options.DefaultModelsExpandDepth(2);
    options.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Model);
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    options.EnableDeepLinking();
    options.DisplayRequestDuration();

    // Swagger UI is intentionally kept public on Railway for API discoverability,
    // but pre-arming the try-it-out form invites reconnaissance of protected
    // endpoints. Dev keeps the one-click try-it-out flow; prod requires an explicit
    // click to switch a given endpoint into execution mode.
    if (app.Environment.IsDevelopment())
    {
        options.EnableTryItOutByDefault();
    }

    // Add custom CSS for better styling
    options.InjectStylesheet("/swagger-ui/custom.css");
});

app.UseCors("CivitiPolicy");
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
app.MapPushTokenEndpoints(); // Push notification token endpoints
app.MapReportEndpoints(); // Report endpoints (issues + comments)
app.MapBlockEndpoints(); // User block/unblock endpoints

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

app.MapGet("/api/health", async (CivitiDbContext context, ISupabaseService supabaseService) =>
    {
        Civiti.Application.Responses.Health.HealthCheckResponse health = new()
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Version = "1.0.0",
            Database = "unknown",
            DatabaseError = null,
            Supabase = "unknown",
            Environment = app.Environment.EnvironmentName
        };

        try
        {
            // Test database connectivity with timeout
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await context.Database.CanConnectAsync(cts.Token);
            health.Database = "connected";
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Database health check timed out after 5 seconds");
            health.Database = "timeout";
            health.DatabaseError = "Connection timeout (5s)";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database health check failed");
            health.Database = "disconnected";
            health.DatabaseError = ex.Message;
        }

        try
        {
            // Test Supabase connectivity
            var supabaseHealthy = await supabaseService.CheckHealthAsync();
            health.Supabase = supabaseHealthy ? "connected" : "disconnected";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Supabase health check failed");
            health.Supabase = "disconnected";
        }

        health.Status = health.Database == "connected" ? "Healthy" : "Degraded";
        return Results.Ok(health);
    })
    .WithName("HealthCheck")
    .WithTags("Health")
    .WithSummary("Health check endpoint with connectivity tests")
    .WithDescription(
        "Performs health checks on critical dependencies including PostgreSQL database and Supabase authentication service. Returns detailed connectivity status for each component.")
    .Produces<Civiti.Application.Responses.Health.HealthCheckResponse>();

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
            CivitiDbContext context = scope.ServiceProvider.GetRequiredService<CivitiDbContext>();

            // Test connection first with shorter timeout
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
            var canConnect = await context.Database.CanConnectAsync(cts.Token);

            if (!canConnect)
            {
                Log.Warning($"Cannot connect to database on attempt {retry}");
                if (retry < maxRetries)
                {
                    await Task.Delay(delayMs * retry); // Exponential backoff
                    continue;
                }

                // Final retry failed - don't attempt migration
                Log.Error("Database connection failed after all retries - skipping migration");
                break;
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
return;

int GetEnvOrConfigInt(string envVar, string configKey, int defaultValue)
{
    var envValue = Environment.GetEnvironmentVariable(envVar);
    return int.TryParse(envValue, out var result) ? result : builder.Configuration.GetValue(configKey, defaultValue);
}

string? GetEnvOrConfig(string envVar, string configKey)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    return !string.IsNullOrWhiteSpace(value) ? value : builder.Configuration[configKey];
}

bool GetEnvOrConfigBool(string envVar, string configKey, bool defaultValue)
{
    var envValue = Environment.GetEnvironmentVariable(envVar);
    return bool.TryParse(envValue, out var result) ? result : builder.Configuration.GetValue(configKey, defaultValue);
}

namespace Civiti.Api
{
    partial class Program
    {
        [GeneratedRegex(@"://([^:]+):([^@]+)@")]
        public static partial Regex MyRegex();
    }
}
