using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
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

// Database — shared Postgres with Civiti.Api and Civiti.Mcp. Civiti.Auth never runs
// migrations (architecture.md §3): Civiti.Api stays the sole migration runner; we read/write
// via DI but never call Database.Migrate(). The OpenIddict tables + McpSessions landed in the
// AddOpenIddictAndMcpSessions migration.
var connectionString = ResolveConnectionString(builder);
builder.Services.AddDbContext<CivitiDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly(typeof(CivitiDbContext).Assembly.GetName().Name);
        npgsql.CommandTimeout(30);
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
    });
    options.UseOpenIddict();
    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
    options.EnableDetailedErrors(builder.Environment.IsDevelopment());
});

// OpenIddict Server — per auth-design.md §3/§4/§8. Scope/flow/lifetime values are the spec
// defaults. v1a ships infrastructure only: /authorize and /token are registered but stubbed
// with 501 because the Supabase login delegation + consent screen land in v1b.
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<CivitiDbContext>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("authorize")
               .SetTokenEndpointUris("token")
               .SetRevocationEndpointUris("revoke");

        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow();

        // MCP clients are all native/loopback or claimed-URL-scheme clients; PKCE is mandatory.
        options.RequireProofKeyForCodeExchange();

        options.RegisterScopes(
            "civiti.read",
            "civiti.write",
            "civiti.admin.read",
            "civiti.admin.write");

        // Ephemeral keys rotate on restart. That's fine because refresh tokens are server-side
        // reference values (opaque + hashed), so losing signing material only invalidates the
        // short-lived access tokens; the next refresh re-issues against the new JWKS. A
        // persisted-key story lands if and when restart-induced reauth becomes user-visible.
        options.AddEphemeralEncryptionKey()
               .AddEphemeralSigningKey();

        // RFC 9068 JWT access tokens so Civiti.Mcp's Validation stack can verify statelessly
        // against our JWKS; refresh tokens remain opaque (OpenIddict default).
        options.DisableAccessTokenEncryption();

        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
               .SetRefreshTokenLifetime(TimeSpan.FromDays(30));

        var aspnet = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough();

        if (builder.Environment.IsDevelopment())
        {
            // Railway terminates TLS at the edge and forwards HTTP to the container with
            // X-Forwarded-Proto: https — our proxy-trust middleware rewrites Request.Scheme
            // accordingly, so production requests reach OpenIddict marked HTTPS. Local dev
            // has no proxy, so allow plaintext to keep the inner-loop simple.
            aspnet.DisableTransportSecurityRequirement();
        }
    });

// Allow-list seed runs once at startup and ensures every client in auth-design.md §6 exists in
// OpenIddict's application store. Idempotent: on second boot it's a no-op.
builder.Services.AddHostedService<Civiti.Auth.Startup.ClientAllowListSeeder>();

var app = builder.Build();

// Proxy trust — same rules as Civiti.Mcp / Civiti.Api; see Civiti.Mcp/Program.cs for the
// observed Railway chain + rationale. Inlined per architecture.md §3 (no speculative
// Civiti.Web library until duplication warrants it).
const int RailwayAppendedHopCount = 2;
IPNetwork[] trustedProxyRanges =
[
    IPNetwork.Parse("100.64.0.0/10"),
    IPNetwork.Parse("127.0.0.0/8"),
    IPNetwork.Parse("::1/128")
];

app.Use(async (context, next) =>
{
    var upstream = context.Connection.RemoteIpAddress;
    if (upstream is { IsIPv4MappedToIPv6: true })
    {
        upstream = upstream.MapToIPv4();
    }

    if (upstream is not null && trustedProxyRanges.Any(n => n.Contains(upstream)))
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues) && xffValues.Count > 0)
        {
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
        Log.Warning(ex, "Health check failed");
        object body = env.IsDevelopment()
            ? new { status = "Degraded", database = "disconnected", error = ex.Message }
            : new { status = "Degraded", database = "disconnected" };
        return Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).ExcludeFromDescription();

// v1a stubs — /authorize and /token answer OpenIddict's passthrough with 501. The Supabase
// login delegation, consent Razor page, and refresh-token rotation wiring land in v1b and v1c.
// Until then, hitting these endpoints is expected to 501; discovery metadata
// (/.well-known/openid-configuration, /.well-known/jwks.json) is already served by OpenIddict.
app.MapMethods("/authorize", ["GET", "POST"], (HttpContext context) =>
{
    Log.Information("/authorize hit but v1a stub has no Supabase login delegation; returning 501.");
    return Results.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Authorization flow not yet implemented",
        detail: "Civiti.Auth v1a ships the OpenIddict/OAuth skeleton only. Supabase login delegation lands in v1b.");
});

app.MapMethods("/token", ["POST"], (HttpContext context) =>
{
    Log.Information("/token hit but v1a stub has no issued codes/tokens; returning 501.");
    return Results.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Token endpoint not yet implemented",
        detail: "Civiti.Auth v1a has no issued authorization codes or refresh tokens to exchange.");
});

app.MapMethods("/revoke", ["POST"], (HttpContext context) =>
{
    Log.Information("/revoke hit; v1a stub acknowledges but there are no active tokens to revoke.");
    return Results.NoContent();
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
Log.Information("Civiti.Auth starting on port {Port}; OpenIddict Server stubbed (v1a — no login flow wired).", port);
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
