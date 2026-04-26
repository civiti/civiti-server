using System.Text.Json;
using System.Text.Json.Serialization;
using Civiti.Application.Services;
using Civiti.Auth.Authentication;
using Civiti.Auth.Endpoints;
using Civiti.Infrastructure.Configuration;
using Civiti.Infrastructure.Data;
using Civiti.Infrastructure.Services.Supabase;
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
// SUPABASE_URL / SUPABASE_PUBLISHABLE_KEY / SUPABASE_SERVICE_KEY between services. The
// publishable key drives the password / OAuth-code exchanges on /auth/v1/token; the service
// key is required from v1b.2 onward for the refresh-time Admin API re-validation in
// SupabaseAdminClient.GetUserAsync (auth-design.md §4 — confirm user exists + role unchanged
// on every refresh).
var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")
    ?? builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException(
        "SUPABASE_URL env var (or Supabase:Url config) is required for v1b login delegation.");
var supabasePublishableKey = Environment.GetEnvironmentVariable("SUPABASE_PUBLISHABLE_KEY")
    ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
    ?? builder.Configuration["Supabase:PublishableKey"]
    ?? throw new InvalidOperationException(
        "SUPABASE_PUBLISHABLE_KEY env var (or Supabase:PublishableKey config) is required for v1b login delegation.");
var supabaseServiceRoleKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY")
    ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
    ?? builder.Configuration["Supabase:ServiceRoleKey"]
    ?? string.Empty;

// SUPABASE_SERVICE_KEY isn't a hard requirement at boot (we don't want a missing key in dev
// to crash the host), but every refresh-token grant calls SupabaseAdminClient.GetUserAsync,
// which short-circuits to null without it — refreshes silently fail and clients log out
// every 15 minutes. Warn loudly so operators see the misconfiguration in the very first log
// scrape rather than only after users start complaining about constant re-auth.
if (string.IsNullOrWhiteSpace(supabaseServiceRoleKey))
{
    Log.Warning(
        "SUPABASE_SERVICE_KEY is not set — refresh-token grants will return invalid_grant and force re-authentication on every access-token expiry. Set the Supabase service role key on this Civiti.Auth deploy to enable refresh.");
}

builder.Services.AddSingleton(new SupabaseConfiguration
{
    Url = supabaseUrl,
    PublishableKey = supabasePublishableKey,
    ServiceRoleKey = supabaseServiceRoleKey
});

// SupabaseAdminClient needs an AdminNotifyConfiguration for retry/timeout/cache settings even
// though Civiti.Auth never sends admin notifications. We register a minimal config (admin
// notification dispatch is disabled here) so refresh-token re-validation can resolve the
// dependency. The HttpClient name + factory mirrors the Civiti.Api wiring.
builder.Services.AddSingleton(new AdminNotifyConfiguration
{
    Enabled = false,
    AdminListCacheSeconds = 60,
    MaxSupabaseRetries = 2,
    SupabaseTimeoutSeconds = 5,
    SupabasePageSize = 200,
    MaxSupabasePages = 50
});
builder.Services.AddHttpClient(SupabaseAdminClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<ISupabaseAdminClient, SupabaseAdminClient>();

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
//
// Pass the scheme name as the default to AddAuthentication so the AuthenticationMiddleware
// populates HttpContext.User from this cookie on every request — Razor Pages' PageModel.User
// reads from there, and /Consent POST needs the user's sub claim to upsert the
// McpUserClientPreference row. Without a default, User stays anonymous on the consent POST,
// the handler bails into LocalRedirect without writing, and /authorize re-routes back to
// /Consent forever. /authorize itself isn't affected — it explicitly authenticates against the
// cookie scheme via httpContext.AuthenticateAsync — but the Razor pages do not.
builder.Services.AddAuthentication(AuthEndpointConstants.CookieScheme)
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
builder.Services.AddScoped<SupabaseLoginCompletion>();
builder.Services.AddScoped<AdminScopeFilter>();

// Razor Pages host the /Login (provider selection + email/password) and /Consent (per-scope
// approval) screens. Both POST back to themselves and redirect to the original /authorize URL
// on success, so no extra route conventions are needed.
builder.Services.AddRazorPages();

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
        // Late ordering ensures the principal + scopes (and the issued refresh-token id we read
        // from RefreshTokenPrincipal) have been finalised by the time we run.
        options.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(handler =>
            handler.UseScopedHandler<McpSessionWriteHandler>()
                   .SetOrder(int.MaxValue - 100_000));

        // /revoke → McpSessions.RevokedAt sync (auth-design.md §10). Late ordering inside the
        // revocation pipeline so OpenIddict's own RevokeToken handler has already flipped the
        // OpenIddictTokens.Status row before we mirror it onto the view-model.
        options.AddEventHandler<OpenIddictServerEvents.HandleRevocationRequestContext>(handler =>
            handler.UseScopedHandler<McpSessionRevokeHandler>()
                   .SetOrder(int.MaxValue - 100_000));

        // RFC 8252 §8.3 loopback wildcard for native MCP clients. We REPLACE OpenIddict's
        // built-in redirect_uri validators (one for /authorize, one for /token) with versions
        // that accept loopback URIs whose port differs from the registered placeholder. The
        // earlier swap-the-URI approach trips an OpenIddict consistency check that compares
        // the validated URI against Request.RedirectUri and 500s.
        options.RemoveEventHandler(OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor);
        options.RemoveEventHandler(OpenIddictServerHandlers.Exchange.ValidateRedirectUri.Descriptor);
        options.AddEventHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>(handler =>
            handler.UseScopedHandler<LoopbackAwareAuthorizationRedirectUriValidator>()
                   .SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor.Order));
        options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(handler =>
            handler.UseScopedHandler<LoopbackAwareTokenRedirectUriValidator>()
                   .SetOrder(OpenIddictServerHandlers.Exchange.ValidateRedirectUri.Descriptor.Order));
    });

builder.Services.AddScoped<McpSessionWriteHandler>();
builder.Services.AddScoped<McpSessionRevokeHandler>();
builder.Services.AddScoped<LoopbackAwareAuthorizationRedirectUriValidator>();
builder.Services.AddScoped<LoopbackAwareTokenRedirectUriValidator>();

// Allow-list seed runs once at startup and ensures every client in auth-design.md §6 exists in
// OpenIddict's application store. Idempotent: on second boot it's a no-op.
builder.Services.AddHostedService<Civiti.Auth.Startup.ClientAllowListSeeder>();

// Background sweep — every 5 minutes, re-validates active admin-scoped sessions against the
// upstream Supabase user. Closes the gap between refresh-token rotations for long-lived
// sessions where a user might be demoted from admin without re-authenticating soon.
builder.Services.AddHostedService<Civiti.Auth.Startup.McpSessionRoleRevalidationSweep>();

var app = builder.Build();

// Static assets (CSS for /Login + /Consent) live under wwwroot/ — UseStaticFiles() exposes
// them at /css/site.css. UseAuthentication wires the cookie scheme into the pipeline so
// SignInAsync on /supabase-callback (and the Razor pages) reaches the registered handler.
// OpenIddict's own scheme is plumbed via its IStartupFilter and needs no extra wiring.
app.UseStaticFiles();
app.UseAuthentication();

app.MapRazorPages();

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

// v1b.2 — /authorize routes through /Login (no cookie) → /Consent (no preference) →
// OpenIddict SignIn (mints code). /token now handles authorization_code AND refresh_token
// grants; the refresh path re-validates the Supabase user via the Admin API on every
// rotation. /revoke remains handled end-to-end by OpenIddict's built-in handler
// (RFC 7009-compliant 200 for unknown tokens); McpSessions.RevokedAt sync via passthrough
// lands in v1b.3 alongside the background role-revalidation sweep and the loopback
// port-wildcard handler for native Claude clients.
app.MapMethods("/authorize", ["GET", "POST"], AuthorizeEndpoint.HandleAsync);
app.MapGet(AuthEndpointConstants.SupabaseCallbackPath, SupabaseCallbackEndpoint.HandleAsync);
app.MapPost("/token", TokenEndpoint.HandleAsync);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
Log.Information("Civiti.Auth starting on port {Port}; v1b.2 — login + consent UI, refresh-token rotation with Supabase Admin re-validation, admin-scope gating live.", port);
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
