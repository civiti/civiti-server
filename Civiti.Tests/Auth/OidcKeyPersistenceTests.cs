using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Civiti.Auth.Startup;
using FluentAssertions;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Civiti.Tests.Auth;

/// <summary>
/// Restarts a real OpenIddict server around a token store that outlives it, the way Civiti-Auth
/// redeploys around Postgres. Each "restart" is a new server process's worth of state with the same
/// database, so a refresh token only works afterwards if the keys it was encrypted with came back.
/// Runs on Kestrel over loopback, so Civiti.Tests needs no extra test-host package.
/// </summary>
public sealed class OidcKeyPersistenceTests : IDisposable
{
    // Fixed, because the default issuer comes from the request URL and each server gets a new port.
    private static readonly Uri Issuer = new("http://civiti-auth.test/");

    private readonly SqliteConnection _tokenStore = new("DataSource=:memory:");

    public OidcKeyPersistenceTests() => _tokenStore.Open();

    public void Dispose() => _tokenStore.Dispose();

    private sealed class TokenStoreContext(DbContextOptions<TokenStoreContext> options) : DbContext(options);

    private sealed class AuthServer(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public async Task<string> SignInAsync()
        {
            using HttpResponseMessage response = await PostTokenRequestAsync(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.Password,
                [Parameters.Username] = "citizen",
                [Parameters.Password] = "unused",
                [Parameters.Scope] = Scopes.OfflineAccess
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty(Parameters.RefreshToken).GetString()!;
        }

        public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
            PostTokenRequestAsync(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.RefreshToken,
                [Parameters.RefreshToken] = refreshToken
            });

        public async Task<JsonElement[]> GetPublishedKeysAsync()
        {
            using JsonDocument jwks = JsonDocument.Parse(await client.GetStringAsync(".well-known/jwks"));
            return jwks.RootElement.GetProperty("keys").EnumerateArray().Select(key => key.Clone()).ToArray();
        }

        private Task<HttpResponseMessage> PostTokenRequestAsync(Dictionary<string, string> parameters) =>
            client.PostAsync("token", new FormUrlEncodedContent(parameters));

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private async Task<AuthServer> StartServerAsync(OidcKeys? keys)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddDbContext<TokenStoreContext>(options =>
        {
            options.UseSqlite(_tokenStore);
            options.UseOpenIddict();
        });
        builder.Services.AddAuthentication();
        builder.Services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<TokenStoreContext>())
            .AddServer(options =>
            {
                options.SetIssuer(Issuer);
                options.SetTokenEndpointUris("token");
                options.AllowPasswordFlow().AllowRefreshTokenFlow();
                options.AcceptAnonymousClients();
                options.RegisterScopes(Scopes.OfflineAccess);

                // The same registration Civiti.Auth/Program.cs uses.
                options.AddOidcKeys(keys);
                options.DisableAccessTokenEncryption();

                options.UseAspNetCore()
                    .EnableTokenEndpointPassthrough()
                    .DisableTransportSecurityRequirement();
            });

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.MapPost("/token", IssueTokensAsync);

        using (IServiceScope scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TokenStoreContext>().Database.EnsureCreatedAsync();
        }

        await app.StartAsync();
        return new AuthServer(app, new HttpClient { BaseAddress = new Uri($"{app.Urls.Single().TrimEnd('/')}/") });
    }

    private static async Task IssueTokensAsync(HttpContext httpContext)
    {
        const string scheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;
        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()!;

        if (request.IsRefreshTokenGrantType())
        {
            AuthenticateResult refreshed = await httpContext.AuthenticateAsync(scheme);
            await httpContext.SignInAsync(scheme, refreshed.Principal!);
            return;
        }

        ClaimsIdentity identity = new(scheme);
        identity.AddClaim(new Claim(Claims.Subject, "citizen").SetDestinations(Destinations.AccessToken));
        ClaimsPrincipal principal = new(identity);
        principal.SetScopes(request.GetScopes());
        await httpContext.SignInAsync(scheme, principal);
    }

    private static string NewKey()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem()));
    }

    [Fact]
    public async Task RefreshToken_StillWorksAfterRestart_WhenKeysArePersisted()
    {
        string signingKey = NewKey(), encryptionKey = NewKey();
        string refreshToken;
        JsonElement[] keysBeforeRestart;

        await using (AuthServer beforeRestart = await StartServerAsync(OidcKeyLoader.Load(signingKey, encryptionKey, isDevelopment: false)))
        {
            refreshToken = await beforeRestart.SignInAsync();
            keysBeforeRestart = await beforeRestart.GetPublishedKeysAsync();
        }

        await using AuthServer afterRestart = await StartServerAsync(OidcKeyLoader.Load(signingKey, encryptionKey, isDevelopment: false));
        using HttpResponseMessage response = await afterRestart.RefreshAsync(refreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty(Parameters.AccessToken).GetString().Should().NotBeNullOrEmpty();

        // Same kid, so access tokens from before the restart still verify against the new JWKS.
        JsonElement[] keysAfterRestart = await afterRestart.GetPublishedKeysAsync();
        keysAfterRestart.Should().ContainSingle();
        keysAfterRestart[0].GetProperty(JsonWebKeyParameterNames.Kid).GetString()
            .Should().Be(keysBeforeRestart[0].GetProperty(JsonWebKeyParameterNames.Kid).GetString());
    }

    /// <summary>
    /// Pins what production did before keys were persisted, which is what makes the test above mean
    /// something: Civiti-Auth logged this same invalid_grant / ID2004 on a real client's refresh
    /// one minute after a deploy.
    /// </summary>
    [Fact]
    public async Task RefreshToken_IsRejectedAfterRestart_WithEphemeralKeys()
    {
        string refreshToken;
        await using (AuthServer beforeRestart = await StartServerAsync(keys: null))
        {
            refreshToken = await beforeRestart.SignInAsync();
        }

        await using AuthServer afterRestart = await StartServerAsync(keys: null);
        using HttpResponseMessage response = await afterRestart.RefreshAsync(refreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty(Parameters.Error).GetString().Should().Be(Errors.InvalidGrant);
        body.RootElement.GetProperty(Parameters.ErrorUri).GetString().Should().EndWith("ID2004");
    }

    /// <summary>Replacing either key signs everyone out; configuration-guide.md relies on this.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RefreshToken_IsRejected_AfterAKeyIsReplaced(bool replaceSigningKey, bool replaceEncryptionKey)
    {
        string signingKey = NewKey(), encryptionKey = NewKey();
        string refreshToken;
        await using (AuthServer beforeRotation = await StartServerAsync(OidcKeyLoader.Load(signingKey, encryptionKey, isDevelopment: false)))
        {
            refreshToken = await beforeRotation.SignInAsync();
        }

        await using AuthServer afterRotation = await StartServerAsync(OidcKeyLoader.Load(
            replaceSigningKey ? NewKey() : signingKey,
            replaceEncryptionKey ? NewKey() : encryptionKey,
            isDevelopment: false));
        using HttpResponseMessage response = await afterRotation.RefreshAsync(refreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty(Parameters.Error).GetString().Should().Be(Errors.InvalidGrant);
    }

    [Fact]
    public async Task Jwks_PublishesOnlyThePublicHalfOfTheSigningKey()
    {
        await using AuthServer server = await StartServerAsync(OidcKeyLoader.Load(NewKey(), NewKey(), isDevelopment: false));

        JsonElement key = (await server.GetPublishedKeysAsync()).Should().ContainSingle().Subject;

        key.GetProperty(JsonWebKeyParameterNames.Use).GetString().Should().Be("sig");
        key.GetProperty(JsonWebKeyParameterNames.N).GetString().Should().NotBeNullOrEmpty();
        key.TryGetProperty(JsonWebKeyParameterNames.D, out _).Should().BeFalse();
        key.TryGetProperty(JsonWebKeyParameterNames.P, out _).Should().BeFalse();
        key.TryGetProperty(JsonWebKeyParameterNames.Q, out _).Should().BeFalse();
    }
}
