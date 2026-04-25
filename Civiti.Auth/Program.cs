using System.Text.Json;
using System.Text.Json.Serialization;
using Civiti.Auth.Authentication;
using Civiti.Auth.Endpoints;
using Civiti.Infrastructure.Configuration;
using Civiti.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
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

// Proxy trust runs as an IStartupFilter (registered before AddOpenIddict so it executes
// first in the IStartupFilter chain) instead of the inline app.Use(...) we use in
// Civiti.Mcp / Civiti.Api. OpenIddict's ASP.NET Core integration injects its server
// middleware at the top of the pipeline via its own IStartupFilter, so any inline
// app.Use(...) registered after var app = builder.Build() runs *behind* it — Request.Scheme
// would still be "http" when OpenIddict's HTTPS check fires on /.well-known/openid-configuration
// and discovery would 400 with ID2083. See ProxyTrustStartupFilter for the full XFF/XFP rules.
builder.Services.AddSingleton<IStartupFilter, Civiti.Auth.Startup.ProxyTrustStartupFilter>();

// Supabase configuration — same env var contract as Civiti.Api so deploys can share
// SUPABASE_URL / SUPABASE_PUBLISHABLE_KEY between services. v1b only needs the public key
// (used as the `apikey` header on the /auth/v1/token PKCE exchange); the service-role key
// is not required and stays unset on Civiti.Auth.
var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")
    ?? builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException(
        "SUPABASE_URL env var (or Supabase:Url config) is required for v1b login delegation.");
var supabasePublishableKey = Environment.GetEnvironmentVariable("SUPABASE_PUBLISHABLE_KEY")
    ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
    ?? builder.Configuration["Supabase:PublishableKey"]
    ?? throw new InvalidOperationException(
        "SUPABASE_PUBLISHABLE_KEY env var (or Supabase:PublishableKey config) is required for v1b login delegation.");

builder.Services.AddSingleton(new SupabaseConfiguration
{
    Url = supabaseUrl,
    PublishableKey = supabasePublishableKey,
    ServiceRoleKey = string.Empty
});

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
// Persist Data Protection keys via the shared CivitiDbContext so the encrypted PKCE state we
// hand to Supabase survives container restarts (Railway redeploys would otherwise discard the
// in-memory key ring and break any in-flight login). Civiti.Api applies the
// AddDataProtectionKeys migration before Civiti.Auth comes up.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<CivitiDbContext>();

// Cookie scheme for the short-lived Civiti.Auth session that survives between /authorize and
// /supabase-callback (and lets a returning user skip the Supabase round-trip if they hit
// /authorize again from a different MCP client within 30 minutes).
builder.Services.AddAuthentication()
    .AddCookie(AuthEndpointConstants.CookieScheme, options =>
    {
        options.Cookie.Name = "civiti_auth_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // SameAsRequest keeps local dev (HTTP) functional; in prod our proxy-trust middleware
        // sets Request.Scheme = "https" before this cookie is written, so it picks up Secure.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
    });

builder.Services.AddSingleton<SupabasePkceStateProtector>();
builder.Services.AddSingleton<SupabaseTokenValidator>();

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

        // McpSession audit row write happens inside OpenIddict's signin pipeline — see
        // Civiti.Auth/Endpoints/McpSessionWriteHandler.cs for the rationale (avoids the
        // orphan-row failure mode where a SignIn pipeline error left a phantom session).
        // Late ordering ensures the principal + scopes have been finalised by the time we run.
        options.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(handler =>
            handler.UseScopedHandler<McpSessionWriteHandler>()
                   .SetOrder(int.MaxValue - 100_000));
    });

builder.Services.AddScoped<McpSessionWriteHandler>();

// Allow-list seed runs once at startup and ensures every client in auth-design.md §6 exists in
// OpenIddict's application store. Idempotent: on second boot it's a no-op.
builder.Services.AddHostedService<Civiti.Auth.Startup.ClientAllowListSeeder>();

var app = builder.Build();

// Wires the cookie scheme into the request pipeline. Required for `httpContext.SignInAsync`
// on the /supabase-callback handler to reach the registered handler. OpenIddict's own scheme
// is plumbed via its IStartupFilter, so no separate wiring is needed for it here.
app.UseAuthentication();

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

// v1b.1 — real /authorize, /supabase-callback, and /token handlers. Refresh-token rotation
// with Supabase Admin re-validation, the consent Razor page, admin-scope gating, and the
// background role-sweep all arrive in v1b.2. /revoke remains handled end-to-end by
// OpenIddict's built-in handler (RFC 7009-compliant 200 for unknown tokens) until v1b.2
// adds McpSessions.RevokedAt sync via passthrough.
app.MapMethods("/authorize", ["GET", "POST"], AuthorizeEndpoint.HandleAsync);
app.MapGet(AuthEndpointConstants.SupabaseCallbackPath, SupabaseCallbackEndpoint.HandleAsync);
app.MapPost("/token", TokenEndpoint.HandleAsync);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
Log.Information("Civiti.Auth starting on port {Port}; v1b.1 — Supabase login delegation + token mint live; refresh + consent land in v1b.2.", port);
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
