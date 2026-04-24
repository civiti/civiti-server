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
using Microsoft.AspNetCore.HttpOverrides;
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

// Mirror Civiti.Api's proxy handling so RemoteIpAddress is the real client IP, not Railway's hop.
//
// SECURITY — known limitation: clearing KnownIPNetworks + KnownProxies trusts X-Forwarded-For from
// any upstream. In practice traffic reaches this service only through Railway's edge, which appends
// (not overwrites) the header chain — but a caller who can reach the container can still inflate
// the counter keyed on the spoofed value. Mitigations in this PR: (a) middleware rate limiter above
// caps total requests per observed IP, and (b) service-layer 1/IP/issue/hour cooldown still fires.
// Follow-up in the Railway deployment PR: replace the clears with explicit KnownNetworks covering
// Railway's actual forward range, once we can observe it from a running service. Matches Civiti.Api's
// current config for consistency in the meantime.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();

// TEMP DIAGNOSTIC — inserted on branch fix/mcp-proxy-trust to observe the real Railway
// upstream IP and X-Forwarded-For chain from the Production deploy. Runs *before*
// UseForwardedHeaders so Connection.RemoteIpAddress is Railway's immediate hop, not the
// rewritten client IP. Remove in the follow-up commit that sets KnownNetworks tightly.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp/public"))
    {
        var upstream = context.Connection.RemoteIpAddress?.ToString() ?? "(null)";
        var xff = context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues) ? string.Join(",", xffValues!) : "(absent)";
        var xfp = context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var xfpValues) ? string.Join(",", xfpValues!) : "(absent)";
        var fwd = context.Request.Headers.TryGetValue("Forwarded", out var fwdValues) ? string.Join(",", fwdValues!) : "(absent)";
        Log.Information("PROXY-DIAG upstream={Upstream} xff={XForwardedFor} xfp={XForwardedProto} forwarded={Forwarded} path={Path}",
            upstream, xff, xfp, fwd, context.Request.Path.Value);
    }
    await next(context);
});

app.UseForwardedHeaders(forwardedHeaders);

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
