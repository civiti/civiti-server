using System.Security.Claims;
using Civiti.Mcp.Authorization;
using FluentAssertions;
using OpenIddict.Abstractions;

namespace Civiti.Tests.Mcp.Authorization;

public class ScopeClaimExtensionsTests
{
    [Fact]
    public void HasCivitiScope_AnonymousPrincipal_ReturnsFalse()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        user.HasCivitiScope("civiti.read").Should().BeFalse();
    }

    [Fact]
    public void HasCivitiScope_OiScpClaim_MatchesExactValue()
    {
        var user = PrincipalWith((OpenIddictConstants.Claims.Private.Scope, "civiti.read"));

        user.HasCivitiScope("civiti.read").Should().BeTrue();
        user.HasCivitiScope("civiti.write").Should().BeFalse();
    }

    [Fact]
    public void HasCivitiScope_StandardScopeClaim_SpaceSeparated_MatchesAnyToken()
    {
        var user = PrincipalWith((OpenIddictConstants.Claims.Scope, "civiti.read civiti.write"));

        user.HasCivitiScope("civiti.read").Should().BeTrue();
        user.HasCivitiScope("civiti.write").Should().BeTrue();
        user.HasCivitiScope("civiti.admin.read").Should().BeFalse();
    }

    [Fact]
    public void HasCivitiScope_MultipleOiScpClaims_MatchesAny()
    {
        var user = PrincipalWith(
            (OpenIddictConstants.Claims.Private.Scope, "civiti.read"),
            (OpenIddictConstants.Claims.Private.Scope, "civiti.write"));

        user.HasCivitiScope("civiti.read").Should().BeTrue();
        user.HasCivitiScope("civiti.write").Should().BeTrue();
        user.HasCivitiScope("civiti.admin.write").Should().BeFalse();
    }

    [Fact]
    public void HasCivitiScope_AdminScope_DoesNotImplyCitizenRead()
    {
        // Admin scopes are independent of civiti.read per auth-design.md §8 — a token
        // carrying only civiti.admin.read must NOT pass a civiti.read scope check.
        var user = PrincipalWith((OpenIddictConstants.Claims.Private.Scope, "civiti.admin.read"));

        user.HasCivitiScope("civiti.read").Should().BeFalse();
    }

    [Fact]
    public void HasCivitiScope_PartialMatch_DoesNotMatch()
    {
        // Substring isn't a match — "civiti.r" doesn't satisfy "civiti.read".
        var user = PrincipalWith((OpenIddictConstants.Claims.Private.Scope, "civiti.read"));

        user.HasCivitiScope("civiti.r").Should().BeFalse();
        user.HasCivitiScope("civiti.read.extra").Should().BeFalse();
    }

    [Fact]
    public void HasCivitiScope_EmptyRequiredScope_ReturnsFalse()
    {
        var user = PrincipalWith((OpenIddictConstants.Claims.Private.Scope, "civiti.read"));

        user.HasCivitiScope(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void HasCivitiScope_NullPrincipal_Throws()
    {
        ClaimsPrincipal user = null!;

        Action act = () => user.HasCivitiScope("civiti.read");

        act.Should().Throw<ArgumentNullException>();
    }

    private static ClaimsPrincipal PrincipalWith(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var (type, value) in claims)
        {
            identity.AddClaim(new Claim(type, value));
        }
        return new ClaimsPrincipal(identity);
    }
}
