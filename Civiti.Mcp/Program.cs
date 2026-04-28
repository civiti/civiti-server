using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Civiti.Application.Email.Models;
using Civiti.Application.Mcp;
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
using OpenIddict.Validation.AspNetCore;
using Serilog;
using static OpenIddict.Validation.OpenIddictValidationEvents;

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

// v1c authenticated /mcp endpoint — see Civiti.Mcp/docs/auth-design.md.
//
// CIVITI_AUTH_ISSUER is the canonical issuer URL of the Civiti.Auth deploy this resource
// server trusts (e.g. https://civiti-auth-development.up.railway.app/). OpenIddict.Validation
// fetches /.well-known/openid-configuration from there, then the JWKS, and validates JWT
// access-token signatures statelessly — no DB round-trip, no introspection. The 15-minute
// access-token TTL bounds the revocation lag for citizen reads (acceptable per auth-design
// open-question #2; admin scopes have stronger guarantees via the refresh-time + 5-min
// sweep paths in Civiti.Auth).
//
// CIVITI_MCP_PUBLIC_ORIGIN is the scheme+host this Mcp deploy is publicly reachable on
// (e.g. https://civiti-mcp-development.up.railway.app). Used to build the absolute resource
// URL in the RFC 9728 discovery doc and the resource_metadata WWW-Authenticate parameter —
// never trust Request.Host, mirroring the same defense Civiti.Auth uses with
// CIVITI_AUTH_PUBLIC_ORIGIN.
var authIssuer = ResolveAuthIssuer(builder);
var mcpPublicOrigin = ResolveMcpPublicOrigin(builder);
var resourceMetadataUrl = $"{mcpPublicOrigin}/.well-known/oauth-protected-resource";

builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(authIssuer);
        options.AddAudiences(McpResourceIdentifiers.Audience);
        options.UseSystemNetHttp();
        options.UseAspNetCore();

        // RFC 9728 §5.3: a Bearer challenge against a Protected Resource SHOULD include a
        // resource_metadata parameter pointing at the discovery doc, so MCP clients can
        // auto-discover the authorization server (issuer, scopes, …) without out-of-band
        // configuration. OpenIddict.Validation.AspNetCore's AttachWwwAuthenticateHeader
        // (order 50_000) serializes whatever sits on transaction.Response into the header,
        // so we run *before* it (order 40_000) and seed the parameter.
        options.AddEventHandler<ProcessChallengeContext>(handler =>
            handler.UseInlineHandler(context =>
            {
                context.Response["resource_metadata"] = resourceMetadataUrl;
                return default;
            })
            .SetOrder(40_000));
    });

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddAuthorization();

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
    .WithTools<PublicStaticDataTools>()
    // Diagnostic — exposed on both /mcp and /mcp/public. On the public mount it returns
    // {authenticated: false}; on /mcp it dumps the validated principal so we can confirm
    // the JWT round-trip end-to-end without needing PR 2's user-scoped tools.
    .WithTools<WhoAmITools>();

var app = builder.Build();

// Proxy trust — resolve the real client IP from X-Forwarded-For without trusting headers from
// arbitrary upstreams. Verified 2026-04-24 against the live Railway deploy via the PR #91
// diagnostic, including a probe that sent a spoofed `X-Forwarded-For: 1.2.3.4`:
//
//   no spoof: upstream=100.64.0.3  xff="<client>, <railway-lb>"
//   spoofed:  upstream=100.64.0.4  xff="1.2.3.4, <client>, <railway-lb>"
//
// Railway **preserves and appends** — client-supplied XFF entries survive unmodified, then
// Railway's edge adds its own two hops (LB's view of the TCP peer, then the internal hop).
// So the leftmost entry is attacker-controllable; the real client IP is at position
// `len - RailwayAppendedHopCount` from the left, i.e. second from right. Everything left of
// that is attacker-supplied and must be ignored.
//
// We reject headers entirely when the direct socket peer isn't in a trusted range. Any direct
// caller that bypassed Railway's edge lands outside CGNAT, and their spoofed headers have no
// effect. The built-in ForwardedHeadersMiddleware isn't used because its peel algorithm stops
// at the first untrusted IP in the chain, which on Railway is the GCP-backed LB (IP range
// effectively unbounded) — that would give us the LB IP instead of the real client.
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
    // before range matching so local dev (where the trusted peer is the kernel itself) still
    // goes through the trust path.
    if (upstream is { IsIPv4MappedToIPv6: true })
    {
        upstream = upstream.MapToIPv4();
    }

    if (upstream is not null && trustedProxyRanges.Any(n => n.Contains(upstream)))
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues) && xffValues.Count > 0)
        {
            // StringValues.ToString() joins multi-header-line values with commas — RFC 7230
            // treats repeated XFF headers as one logical list, so we concatenate before
            // splitting. Parsing only xffValues[0] would miss entries from later header lines
            // and let the hop-count index land on an attacker-controlled entry.
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
            // Every Railway hop stamps X-Forwarded-Proto with the scheme it observed, so the
            // rightmost entry (across all header lines) is Railway's authoritative view; anything
            // to its left could be client-supplied and spoofable.
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

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// RFC 9728 Protected-Resource Metadata. Anonymous; rate-limited under the same per-IP policy
// as the public MCP endpoint so a discovery flood can't drown the host. The "resource" value
// is the *protected* endpoint (/mcp), not the metadata URL itself — clients use it to verify
// the discovery doc applies to the resource they were challenged for.
//
// Per RFC 8414 §2 the issuer identifier (and by extension the value advertised in
// authorization_servers per RFC 9728) is a URL with NO trailing slash — that's also what
// Civiti.Auth puts in the JWT `iss` claim. We trim the slash that ResolveAuthIssuer added
// for OpenIddict's discovery-URL concat so a strict client doing string equality between
// `aud` and the discovered AS doesn't reject the match over a stray "/".
//
// Cache-Control: this document changes only on a Civiti.Auth issuer move or a scope
// addition, so a 5-minute cache balances client efficiency against propagation lag if we
// ever do change it.
var authorizationServer = authIssuer.ToString().TrimEnd('/');
app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=300";
    return Results.Json(new
    {
        resource = $"{mcpPublicOrigin}/mcp",
        authorization_servers = new[] { authorizationServer },
        scopes_supported = new[]
        {
            "civiti.read",
            "civiti.write",
            "civiti.admin.read",
            "civiti.admin.write"
        },
        bearer_methods_supported = new[] { "header" }
    });
})
.AllowAnonymous()
.ExcludeFromDescription()
.RequireRateLimiting(McpPublicRateLimitPolicy);

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
})
.ExcludeFromDescription()
.RequireRateLimiting(McpPublicRateLimitPolicy);

app.MapMcp("/mcp/public").RequireRateLimiting(McpPublicRateLimitPolicy);

// Authenticated mount. RequireAuthorization() with no policy name = "any authenticated
// principal"; per-scope policies land in PR 2 alongside the §2.1 user-scoped tools that
// need them. Same per-IP rate-limit policy as the public mount for now; PR 2+ may
// partition by sub for higher per-user ceilings.
app.MapMcp("/mcp").RequireAuthorization().RequireRateLimiting(McpPublicRateLimitPolicy);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8081";
Log.Information(
    "Civiti.Mcp starting on port {Port}; /mcp/public (anonymous), /mcp (bearer, issuer={Issuer}, audience={Audience})",
    port, authIssuer, McpResourceIdentifiers.Audience);
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
    return $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={username};Password={password};SSL Mode=Require;Timeout=30;Command Timeout=30;Connection Idle Lifetime=300;Maximum Pool Size=50{includeErrorDetail}";
}

static Uri ResolveAuthIssuer(WebApplicationBuilder builder)
{
    var raw = Environment.GetEnvironmentVariable("CIVITI_AUTH_ISSUER")
        ?? builder.Configuration["Auth:Issuer"]
        ?? throw new InvalidOperationException(
            "CIVITI_AUTH_ISSUER env var (or Auth:Issuer config) must be set to the Civiti.Auth deploy URL, e.g. https://civiti-auth-development.up.railway.app/");

    if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException(
            $"CIVITI_AUTH_ISSUER must be an absolute URL with scheme. Got: '{raw}'.");
    }
    if (uri.Scheme is not ("http" or "https"))
    {
        throw new InvalidOperationException(
            $"CIVITI_AUTH_ISSUER must use http or https. Got: '{uri.Scheme}'.");
    }

    // OpenIddict's discovery client appends ".well-known/openid-configuration" to the issuer
    // by string concat; a missing trailing slash truncates the path segment. Normalise here so
    // a misconfigured env var ("…/up.railway.app" vs "…/up.railway.app/") doesn't silently
    // fail discovery.
    if (!uri.AbsolutePath.EndsWith('/'))
    {
        uri = new Uri(uri.AbsoluteUri + "/");
    }
    return uri;
}

static string ResolveMcpPublicOrigin(WebApplicationBuilder builder)
{
    var raw = Environment.GetEnvironmentVariable("CIVITI_MCP_PUBLIC_ORIGIN")
        ?? builder.Configuration["Mcp:PublicOrigin"]
        ?? throw new InvalidOperationException(
            "CIVITI_MCP_PUBLIC_ORIGIN env var (or Mcp:PublicOrigin config) must be set to this Mcp deploy's public scheme+host, e.g. https://civiti-mcp-development.up.railway.app");

    if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException(
            $"CIVITI_MCP_PUBLIC_ORIGIN must be an absolute URL with scheme. Got: '{raw}'.");
    }
    if (uri.Scheme is not ("http" or "https"))
    {
        throw new InvalidOperationException(
            $"CIVITI_MCP_PUBLIC_ORIGIN must use http or https. Got: '{uri.Scheme}'.");
    }
    // Reject a non-root path: the env var documents a scheme+host origin, so a trailing path
    // component (e.g. "/v1") is almost certainly a misconfiguration. Without this the
    // GetLeftPart(Authority) call below would silently strip it, producing the wrong
    // `resource` value in the discovery doc and a mismatched `resource_metadata` parameter
    // in WWW-Authenticate — both quietly wrong rather than loudly invalid.
    if (uri.AbsolutePath is not ("" or "/"))
    {
        throw new InvalidOperationException(
            $"CIVITI_MCP_PUBLIC_ORIGIN must be a scheme+host origin with no path. Got: '{raw}'.");
    }

    // Trim trailing slash so callers can format "$origin/mcp" without doubling.
    return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
}
