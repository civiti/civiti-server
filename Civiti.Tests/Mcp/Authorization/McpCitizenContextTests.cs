using System.Security.Claims;
using Civiti.Application.Services;
using Civiti.Domain.Exceptions;
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

    // ── RequireCitizenWriteAsync ───────────────────────────────────────────

    [Fact]
    public async Task RequireCitizenWriteAsync_NoScope_RejectsMissingScope()
    {
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc"));

        var result = await sut.RequireCitizenWriteAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("missing_scope");
    }

    [Fact]
    public async Task RequireCitizenWriteAsync_ReadOnlyScope_RejectsMissingScope()
    {
        // Scopes are independent per auth-design.md §8 — civiti.read does NOT imply
        // civiti.write. A token with only read must be rejected from write tools.
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc", scope: "civiti.read"));

        var result = await sut.RequireCitizenWriteAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("missing_scope");
    }

    [Fact]
    public async Task RequireCitizenWriteAsync_WriteOnlyScope_Authorizes()
    {
        // Symmetric: a write-only token is enough to call write tools. The system doesn't
        // require both scopes for write tools because nothing in the §2.2 surface needs to
        // also call read tools internally — the response payloads are self-contained.
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc", scope: "civiti.write"));

        var result = await sut.RequireCitizenWriteAsync();

        result.Authorized.Should().BeTrue();
        result.Context!.SupabaseUserId.Should().Be("abc");
        _userService.Verify(s => s.GetUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RequireCitizenWriteAsync_BothScopes_Authorizes()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, "abc-456"));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Scope, "civiti.read civiti.write"));
        var sut = CreateSut(new ClaimsPrincipal(identity));

        var result = await sut.RequireCitizenWriteAsync();

        result.Authorized.Should().BeTrue();
        result.Context!.SupabaseUserId.Should().Be("abc-456");
    }

    [Fact]
    public async Task ResolveCitizenWriteAsync_WriteScopeAndProfile_ResolvesInternalId()
    {
        var internalId = Guid.NewGuid();
        _userService.Setup(s => s.GetUserIdAsync("abc")).ReturnsAsync(internalId);
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc", scope: "civiti.write"));

        var result = await sut.ResolveCitizenWriteAsync();

        result.Authorized.Should().BeTrue();
        result.Context!.InternalUserId.Should().Be(internalId);
    }

    [Fact]
    public async Task ResolveCitizenWriteAsync_NoUserProfile_RejectsUserProfileMissing()
    {
        _userService.Setup(s => s.GetUserIdAsync("abc")).ReturnsAsync((Guid?)null);
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc", scope: "civiti.write"));

        var result = await sut.ResolveCitizenWriteAsync();

        result.Authorized.Should().BeFalse();
        ReasonOf(result).Should().Be("user_profile_missing");
    }

    // ── TryResolveCitizenAsync (best-effort, no scope check) ──

    [Fact]
    public async Task TryResolveCitizenAsync_AnonymousPrincipal_ReturnsNull()
    {
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await sut.TryResolveCitizenAsync();

        result.Should().BeNull();
        _userService.Verify(s => s.GetUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryResolveCitizenAsync_NoHttpContext_ReturnsNull()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);
        var sut = new McpCitizenContext(accessor.Object, _userService.Object, NullLogger<McpCitizenContext>.Instance);

        var result = await sut.TryResolveCitizenAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryResolveCitizenAsync_AuthenticatedNoSub_ReturnsNull()
    {
        // Authenticated identity with no sub claim — treat as anonymous for personalization,
        // don't surface an error (caller falls back to public-tier data).
        var identity = new ClaimsIdentity("test");
        var sut = CreateSut(new ClaimsPrincipal(identity));

        var result = await sut.TryResolveCitizenAsync();

        result.Should().BeNull();
        _userService.Verify(s => s.GetUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryResolveCitizenAsync_AuthenticatedSubButNoProfile_ReturnsNull()
    {
        // Token valid, sub present, but no UserProfile row (Google sign-in without onboarding).
        // Best-effort path returns null — don't fail the read tool, just skip personalization.
        _userService.Setup(s => s.GetUserIdAsync("abc-123")).ReturnsAsync((Guid?)null);
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-123"));

        var result = await sut.TryResolveCitizenAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryResolveCitizenAsync_AuthenticatedAndResolvable_ReturnsInternalGuid()
    {
        var internalId = Guid.NewGuid();
        _userService.Setup(s => s.GetUserIdAsync("abc-123")).ReturnsAsync(internalId);
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-123"));

        var result = await sut.TryResolveCitizenAsync();

        result.Should().Be(internalId);
    }

    [Fact]
    public async Task TryResolveCitizenAsync_AuthenticatedWithoutCivitiReadScope_StillResolves()
    {
        // No scope check on this helper — block-list filtering is identity-based, not
        // permission-based. A token with civiti.write only should still get personalization.
        var internalId = Guid.NewGuid();
        _userService.Setup(s => s.GetUserIdAsync("abc-456")).ReturnsAsync(internalId);
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-456", scope: "civiti.write"));

        var result = await sut.TryResolveCitizenAsync();

        result.Should().Be(internalId);
    }

    [Fact]
    public async Task TryResolveCitizenAsync_AccountSoftDeleted_ReturnsNull()
    {
        // UserService.GetUserIdAsync throws AccountDeletedException for IsDeleted=true
        // profiles. The best-effort contract on TryResolveCitizenAsync requires the
        // caller to fall through to anonymous (null) rather than surfacing a 500 to
        // the agent.
        _userService.Setup(s => s.GetUserIdAsync("abc-deleted", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccountDeletedException());
        var sut = CreateSut(AuthenticatedPrincipal(sub: "abc-deleted"));

        var result = await sut.TryResolveCitizenAsync();

        result.Should().BeNull();
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
