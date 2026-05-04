using Civiti.Auth.Endpoints;
using FluentAssertions;
using OpenIddict.Abstractions;

namespace Civiti.Tests.Auth;

public class LoopbackRedirectUriMatcherTests
{
    [Theory]
    [InlineData("http://127.0.0.1/callback")]
    [InlineData("http://127.0.0.1:0/callback")]
    [InlineData("http://127.0.0.1:54321/callback")]
    [InlineData("http://[::1]/callback")]
    [InlineData("http://[::1]:8080/cb")]
    public void IsLoopback_RecognizesIpv4AndIpv6Loopback(string uri)
    {
        LoopbackRedirectUriMatcher.IsLoopback(uri).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://localhost/callback")]
    [InlineData("http://localhost:33418/oauth/callback")]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://example.com/cb")]
    [InlineData("not a uri")]
    [InlineData("")]
    public void IsLoopback_RejectsNonIpLoopbackHosts(string uri)
    {
        // localhost is excluded on purpose (RFC 8252 §7.3 recommends against it). Any DNS
        // hostname — even an allowlisted one — fails IsLoopback; the allowlist is a separate
        // path checked by IsAcceptableDcrRedirectUri.
        LoopbackRedirectUriMatcher.IsLoopback(uri).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://127.0.0.1:54321/callback")]
    [InlineData("http://[::1]:0/cb")]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    public void IsAcceptableDcrRedirectUri_AcceptsLoopbackOrAllowlisted(string uri)
    {
        LoopbackRedirectUriMatcher.IsAcceptableDcrRedirectUri(uri).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://localhost:33418/oauth/callback")] // localhost not loopback per RFC 8252 §7.3
    [InlineData("http://evil.com/callback")]              // arbitrary http:// host
    [InlineData("https://evil.example.com/cb")]           // arbitrary https:// host
    [InlineData("https://Claude.ai/api/mcp/auth_callback")] // case-sensitive allowlist match
    [InlineData("https://claude.ai/api/mcp/auth_callback?evil=1")] // suffix injection
    [InlineData("https://claude.ai/api/mcp/auth_callback#evil")]   // fragment injection
    [InlineData("file:///etc/passwd")]
    [InlineData("not a uri")]
    [InlineData("")]
    public void IsAcceptableDcrRedirectUri_RejectsEverythingElse(string uri)
    {
        LoopbackRedirectUriMatcher.IsAcceptableDcrRedirectUri(uri).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://127.0.0.1:0/cb")]
    [InlineData("http://[::1]:54321/cb")]
    public void DeriveApplicationType_LoopbackOnly_ReturnsNative(string uri)
    {
        LoopbackRedirectUriMatcher.DeriveApplicationType(new[] { uri })
            .Should().Be(OpenIddictConstants.ApplicationTypes.Native);
    }

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://example.com/cb")]
    public void DeriveApplicationType_HttpsOnly_ReturnsWeb(string uri)
    {
        LoopbackRedirectUriMatcher.DeriveApplicationType(new[] { uri })
            .Should().Be(OpenIddictConstants.ApplicationTypes.Web);
    }

    [Fact]
    public void DeriveApplicationType_LoopbackPlusHttps_ReturnsNative()
    {
        // Mixed — loopback presence implies a native/installed component, so RFC 7591
        // "native" is the right label even when an HTTPS callback is also registered.
        LoopbackRedirectUriMatcher.DeriveApplicationType(
            new[] { "https://claude.ai/api/mcp/auth_callback", "http://127.0.0.1:0/cb" })
            .Should().Be(OpenIddictConstants.ApplicationTypes.Native);
    }

    [Fact]
    public void DeriveApplicationType_NonHttpsNonLoopback_ReturnsNative()
    {
        // Defensive: anything that isn't HTTPS and isn't loopback (custom URI scheme,
        // malformed string, http://...) falls back to "native". RegisterEndpoint will have
        // already rejected non-acceptable URIs by this point, so this is unreachable in
        // practice — but the helper still returns a valid RFC 7591 value either way.
        LoopbackRedirectUriMatcher.DeriveApplicationType(new[] { "cursor://anysphere/sso" })
            .Should().Be(OpenIddictConstants.ApplicationTypes.Native);
    }

    [Fact]
    public void DeriveApplicationType_Empty_ReturnsNative()
    {
        LoopbackRedirectUriMatcher.DeriveApplicationType(Array.Empty<string>())
            .Should().Be(OpenIddictConstants.ApplicationTypes.Native);
    }

    [Fact]
    public void AllowlistedDcrRedirectUris_ContainsClaudeRelay()
    {
        // Hard-coded sentinel: this entry MUST NOT be removed without a coordinated rollout
        // (Claude Desktop's connect flow can't complete without it). Adding new entries is
        // a security-sensitive operation — every entry must be a first-party host the
        // client vendor itself controls.
        LoopbackRedirectUriMatcher.AllowlistedDcrRedirectUris
            .Should().Contain("https://claude.ai/api/mcp/auth_callback");
    }
}
