using Civiti.Auth.Endpoints;
using FluentAssertions;

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
