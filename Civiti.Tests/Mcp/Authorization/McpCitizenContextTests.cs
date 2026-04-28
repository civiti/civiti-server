using System.Security.Claims;
using Civiti.Application.Services;
using Civiti.Mcp.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenIddict.Abstractions;

namespace Civiti.Tests.Mcp.Authorization;

public class McpCitizenContextTests
{
    private readonly Mock<IUserService> _userService = new();

    [Fact]
    public async Task RequireCitizenReadAsync_AnonymousPrincipal_RejectsUnauthenticated()
    {
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await sut.RequireCitizenReadAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("unauthenticated");
        _userService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RequireCitizenReadAsync_NoHttpContext_RejectsUnauthenticated()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);
        var sut = new McpCitizenContext(accessor.Object, _userService.Object, NullLogger<McpCitizenContext>.Instance);

        var result = await sut.RequireCitizenReadAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("unauthenticated");
    }

    [Fact]
    public async Task RequireCitizenReadAsync_AuthenticatedNoScope_RejectsMissingScope()
    {
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-123"));

        var result = await sut.RequireCitizenReadAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("missing_scope");
        _userService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RequireCitizenReadAsync_WriteScopeOnly_RejectsMissingScope()
    {
        // civiti.write does not imply civiti.read in our scope model (auth-design.md §8).
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc", scope: "civiti.write"));

        var result = await sut.RequireCitizenReadAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("missing_scope");
    }

    [Fact]
    public async Task RequireCitizenReadAsync_ReadScopeButNoSub_RejectsMissingSubject()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Private.Scope, "civiti.read"));
        var sut = CreateSut(new ClaimsPrincipal(identity));

        var result = await sut.RequireCitizenReadAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("missing_subject");
    }

    [Fact]
    public async Task RequireCitizenReadAsync_ValidPrincipal_ReturnsContextWithoutDbHit()
    {
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-123", scope: "civiti.read"));

        var result = await sut.RequireCitizenReadAsync();

        result.Authorized.Should().BeTrue();
        result.Context!.SupabaseUserId.Should().Be("abc-123");
        // The CitizenContext type from RequireCitizenReadAsync deliberately doesn't carry an
        // internal id — the type system enforces "you must call ResolveCitizenAsync to get one".
        // RequireCitizenReadAsync MUST NOT call GetUserIdAsync — that's the whole point of
        // the two-method split (avoid one DB round-trip when the tool only needs the sub).
        _userService.Verify(s => s.GetUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveCitizenAsync_ValidPrincipal_ResolvesInternalIdViaUserService()
    {
        var internalId = Guid.NewGuid();
        _userService.Setup(s => s.GetUserIdAsync("abc-123")).ReturnsAsync(internalId);
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-123", scope: "civiti.read"));

        var result = await sut.ResolveCitizenAsync();

        result.Authorized.Should().BeTrue();
        result.Context!.SupabaseUserId.Should().Be("abc-123");
        result.Context.InternalUserId.Should().Be(internalId);
    }

    [Fact]
    public async Task ResolveCitizenAsync_NoUserProfile_RejectsUserProfileMissing()
    {
        _userService.Setup(s => s.GetUserIdAsync("abc-123")).ReturnsAsync((Guid?)null);
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-123", scope: "civiti.read"));

        var result = await sut.ResolveCitizenAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("user_profile_missing");
    }

    [Fact]
    public async Task ResolveCitizenAsync_MissingScope_DoesNotHitDb()
    {
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc", scope: "civiti.write"));

        var result = await sut.ResolveCitizenAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("missing_scope");
        _userService.Verify(s => s.GetUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RequireCitizenReadAsync_ReadScopeViaStandardScopeClaim_Authorizes()
    {
        // OpenIddict.Validation can surface scopes via either oi_scp or the standard "scope"
        // claim. Both must work; this exercises the "scope" path that the previous test
        // handled via oi_scp.
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, "abc-123"));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Scope, "civiti.read civiti.write"));
        var sut = CreateSut(new ClaimsPrincipal(identity));

        var result = await sut.RequireCitizenReadAsync();

        result.Authorized.Should().BeTrue();
        result.Context!.SupabaseUserId.Should().Be("abc-123");
    }

    private McpCitizenContext CreateSut(ClaimsPrincipal user)
    {
        var ctx = new DefaultHttpContext { User = user };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(ctx);
        return new McpCitizenContext(accessor.Object, _userService.Object, NullLogger<McpCitizenContext>.Instance);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(string sub, string? scope = null)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, sub));
        if (scope is not null)
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Private.Scope, scope));
        }
        return new ClaimsPrincipal(identity);
    }

    private static string ReasonOf<TContext>(CitizenAuthResult<TContext> result) where TContext : class
    {
        // ErrorPayload is an anonymous object: { ok = false, reason = "...", message = "..." }.
        // Use reflection to read the reason without coupling tests to a serialization format.
        var payload = result.ErrorPayload!;
        var prop = payload.GetType().GetProperty("reason")!;
        return (string)prop.GetValue(payload)!;
    }
}
