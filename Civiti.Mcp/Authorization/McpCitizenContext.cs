using Civiti.Application.Services;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace Civiti.Mcp.Authorization;

public sealed class McpCitizenContext(
    IHttpContextAccessor httpContextAccessor,
    IUserService userService,
    ILogger<McpCitizenContext> logger) : IMcpCitizenContext
{
    private const string CivitiReadScope = "civiti.read";

    public async Task<CitizenAuthResult<CitizenContext>> RequireCitizenReadAsync(CancellationToken cancellationToken = default)
    {
        var (sub, error) = AuthenticateAndExtractSub();
        if (error is not null)
        {
            return new CitizenAuthResult<CitizenContext>(null, error);
        }
        return CitizenAuthResult<CitizenContext>.FromContext(new CitizenContext(sub!));
    }

    public async Task<CitizenAuthResult<IdentifiedCitizenContext>> ResolveCitizenAsync(CancellationToken cancellationToken = default)
    {
        var (sub, error) = AuthenticateAndExtractSub();
        if (error is not null)
        {
            return new CitizenAuthResult<IdentifiedCitizenContext>(null, error);
        }

        var internalId = await userService.GetUserIdAsync(sub!);
        if (internalId is null)
        {
            // The token is valid but no UserProfile row matches the sub. That happens for
            // identities that authenticated via Google but never completed onboarding — the
            // same gap the existing /api/users/me path returns 404 on. Surface it as a
            // structured tool error so the agent can explain.
            logger.LogWarning("MCP tool call rejected: no UserProfile for authenticated sub {Sub}", sub);
            return CitizenAuthResult<IdentifiedCitizenContext>.Rejected(
                "user_profile_missing",
                "No Civiti user profile is linked to this account yet — finish signup in the app first.");
        }

        return CitizenAuthResult<IdentifiedCitizenContext>.FromContext(
            new IdentifiedCitizenContext(sub!, internalId.Value));
    }

    /// <summary>
    /// Shared first half of both public methods: validate the principal, enforce the read
    /// scope, extract the sub. Returns a tuple — either <c>Sub</c> is non-null (authenticated
    /// successfully) or <c>Error</c> is non-null (caller wraps it in the typed result).
    /// </summary>
    private (string? Sub, object? Error) AuthenticateAndExtractSub()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity is not { IsAuthenticated: true })
        {
            // Defense in depth: /mcp is mounted with RequireAuthorization() so the auth pipeline
            // returns 401 before tools execute. This branch only fires if a future change drops
            // that or wires a tool onto an anonymous mount — better to fail closed loudly.
            return (null, RejectionPayload("unauthenticated", "Bearer token required."));
        }

        if (!user.HasCivitiScope(CivitiReadScope))
        {
            logger.LogWarning(
                "MCP tool call rejected: token for sub {Sub} is missing required scope {Scope}",
                user.FindFirst(OpenIddictConstants.Claims.Subject)?.Value,
                CivitiReadScope);
            return (null, RejectionPayload(
                "missing_scope",
                $"Required scope '{CivitiReadScope}' was not granted on this token."));
        }

        var sub = user.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            // Civiti.Auth always stamps sub on issue. A token reaching here without one is
            // either a misconfigured upstream issuer or a bug in OpenIddict.Validation —
            // refuse and let it surface in logs rather than passing null downstream.
            logger.LogWarning("MCP tool call rejected: validated token has no sub claim");
            return (null, RejectionPayload("missing_subject", "Bearer token has no sub claim."));
        }

        return (sub, null);
    }

    private static object RejectionPayload(string reason, string message) =>
        new { ok = false, reason, message };
}
