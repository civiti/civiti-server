using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Civiti.Application.Email.Models;
using Civiti.Application.Notifications;
using Civiti.Application.Push.Models;
using Civiti.Application.Services;
using Civiti.Infrastructure.Configuration;
using Civiti.Infrastructure.Data;
using Civiti.Infrastructure.Services;
using Civiti.Infrastructure.Services.AdminNotify;
using Civiti.Infrastructure.Services.Email;
using Civiti.Mcp.Tools;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Database — mirrors Civiti.Api's DATABASE_URL parsing so Railway's URL-format var just works.
// Civiti.Mcp never runs migrations: that is Civiti.Api's job (see Civiti.Mcp/docs/architecture.md §3).
var connectionString = ResolveConnectionString(builder);
builder.Services.AddDbContext<CivitiDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly(typeof(CivitiDbContext).Assembly.GetName().Name);
        npgsql.CommandTimeout(30);
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
    })
    .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
    .EnableDetailedErrors(builder.Environment.IsDevelopment()));

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// Channel writers required by the service graph (NotificationService, AdminNotifier).
// No consumers are registered in this host — per architecture.md §3, email/push/admin-notify
// background services run only in Civiti.Api. DropWrite keeps any stray producer from blocking
// if the buffer ever fills; the public tools don't exercise these paths today.
var emailChannel = Channel.CreateBounded<EmailNotification>(
    new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropWrite });
builder.Services.AddSingleton(emailChannel.Reader);
builder.Services.AddSingleton(emailChannel.Writer);

var pushChannel = Channel.CreateBounded<PushNotificationMessage>(
    new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropWrite });
builder.Services.AddSingleton(pushChannel.Reader);
builder.Services.AddSingleton(pushChannel.Writer);

var adminNotifyChannel = Channel.CreateBounded<AdminNotifyRequest>(
    new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropWrite });
builder.Services.AddSingleton(adminNotifyChannel.Reader);
builder.Services.AddSingleton(adminNotifyChannel.Writer);

// Minimal config singletons the service graph needs. Values are defaults — the Mcp host doesn't
// send mail or poke Supabase on the public path.
builder.Services.AddSingleton(new ResendConfiguration
{
    ApiKey = string.Empty,
    FromEmail = "Civiti <noreply@civiti.ro>",
    FrontendBaseUrl = "https://civiti.ro",
    DebounceMinutes = 5
});
builder.Services.AddSingleton(new AdminNotifyConfiguration
{
    Enabled = false,
    ChannelCapacity = 1_000,
    AdminListCacheSeconds = 60,
    MaxSupabaseRetries = 3,
    SupabaseTimeoutSeconds = 10,
    SupabasePageSize = 200,
    MaxSupabasePages = 50
});

// Service graph — the subset IIssueService / IAuthorityService / IGamificationService transitively need.
builder.Services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IGamificationService, GamificationService>();
builder.Services.AddSingleton<IAdminNotifier, AdminNotifier>();
builder.Services.AddScoped<IIssueService, IssueService>();
builder.Services.AddScoped<IAuthorityService, AuthorityService>();

// Rate limiting per tool-inventory.md §1 `read.public` class: 30 requests / min per source IP.
// Defense-in-depth on top of the service-layer per-IP cooldowns — an attacker still has to pay
// the request cost of every spoofed header rotation, and legitimate bursts get a clean 429 with
// Retry-After instead of silent degradation.
const string McpPublicRateLimitPolicy = "mcp-public";
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiter.AddPolicy(McpPublicRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// MCP server with the six §1 public tools (see Civiti.Mcp/docs/tool-inventory.md).
// Stateless transport: the public endpoint has no session, no client state; every request
// carries all the context the handler needs.
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<PublicIssueTools>()
    .WithTools<PublicAuthorityTools>()
    .WithTools<PublicGamificationTools>()
    .WithTools<PublicStaticDataTools>();

var app = builder.Build();

// Proxy trust — resolve the real client IP from X-Forwarded-For without trusting headers from
// arbitrary upstreams. Observed from the PR #91 diagnostic deploy:
//   upstream=100.64.0.3  xff="<client-ip>, <railway-lb-public-ip>"  xfp=https
// Railway fronts every container via an internal edge in the CGNAT range 100.64.0.0/10 (RFC 6598),
// and the edge appends (never overwrites) the X-Forwarded-For chain — the first entry is the
// original client. So: if the direct socket peer is in CGNAT (or loopback, for local dev), take
// XFF[0] as the client IP. Any direct caller that bypassed Railway's edge lands with upstream
// outside CGNAT, and we leave their RemoteIpAddress untouched — header spoofing is harmless in
// that case. The built-in ForwardedHeadersMiddleware isn't used because its peel algorithm stops
// at the first untrusted IP in the chain, which on Railway is the GCP-backed LB (IP range
// effectively unbounded) — that would give us the LB IP instead of the real client.
IPNetwork[] trustedProxyRanges =
[
    IPNetwork.Parse("100.64.0.0/10"), // Railway internal edge (RFC 6598 CGNAT)
    IPNetwork.Parse("127.0.0.0/8"),   // IPv4 loopback (local dev)
    IPNetwork.Parse("::1/128")        // IPv6 loopback
];

app.Use(async (context, next) =>
{
    var upstream = context.Connection.RemoteIpAddress;
    if (upstream is not null && trustedProxyRanges.Any(n => n.Contains(upstream)))
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues) && xffValues.Count > 0)
        {
            var firstEntry = xffValues[0]?.Split(',')[0].Trim();
            if (IPAddress.TryParse(firstEntry, out var clientIp))
            {
                context.Connection.RemoteIpAddress = clientIp;
            }
        }

        if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var xfpValues) && xfpValues.Count > 0)
        {
            var scheme = xfpValues[0]?.Split(',')[0].Trim();
            if (scheme is "http" or "https")
            {
                context.Request.Scheme = scheme;
            }
        }
    }

    await next(context);
});

app.UseRateLimiter();

app.MapGet("/api/health", async (CivitiDbContext ctx, IHostEnvironment env) =>
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connected = await ctx.Database.CanConnectAsync(cts.Token);
        return connected
            ? Results.Ok(new { status = "Healthy", database = "connected" })
            : Results.Json(new { status = "Degraded", database = "disconnected" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        // Log the full detail but only surface ex.Message to callers in Development. Npgsql errors
        // routinely include host/port/connection-string fragments that must not leak to the public.
        Log.Warning(ex, "Health check failed");
        object body = env.IsDevelopment()
            ? new { status = "Degraded", database = "disconnected", error = ex.Message }
            : new { status = "Degraded", database = "disconnected" };
        return Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).ExcludeFromDescription();

app.MapMcp("/mcp/public").RequireRateLimiting(McpPublicRateLimitPolicy);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8081";
Log.Information("Civiti.Mcp starting on port {Port}; /mcp/public (anonymous, {ToolCount} tools)", port, 6);
await app.RunAsync($"http://0.0.0.0:{port}");
return;

static string ResolveConnectionString(WebApplicationBuilder builder)
{
    var raw = Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? builder.Configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException(
            "DATABASE_URL env var or ConnectionStrings:PostgreSQL must be configured.");

    if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://"))
    {
        return raw;
    }

    var uri = new Uri(raw.Replace("postgres://", "postgresql://"));
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = userInfo[0];
    var password = userInfo.Length > 1 ? userInfo[1] : string.Empty;
    var includeErrorDetail = builder.Environment.IsDevelopment() ? ";Include Error Detail=true" : string.Empty;
    return $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;Timeout=30;Command Timeout=30;Connection Idle Lifetime=300;Maximum Pool Size=50{includeErrorDetail}";
}
