using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
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
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.MapGet("/api/health", async (CivitiDbContext ctx) =>
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connected = await ctx.Database.CanConnectAsync(cts.Token);
        return Results.Ok(new { status = connected ? "Healthy" : "Degraded", database = connected ? "connected" : "disconnected" });
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Health check failed");
        return Results.Ok(new { status = "Degraded", database = "disconnected", error = ex.Message });
    }
}).ExcludeFromDescription();

app.MapMcp("/mcp/public");

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
