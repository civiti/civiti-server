using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// Mirrors OpenIddict's /revoke into the <see cref="Civiti.Domain.Entities.McpSession"/> view
/// table so the (forthcoming) "Connected AI Assistants" UI shows the session as revoked the
/// instant the user revokes their token. Without this handler the OpenIddict tokens row would
/// flip to revoked but the McpSession row would still show <see cref="Civiti.Domain.Entities.McpSession.RevokedAt"/>
/// = null, surfacing a phantom-active session that's actually unable to refresh.
///
/// Hook is <see cref="HandleRevocationRequestContext"/> with late ordering so OpenIddict's
/// built-in revoker runs first (writes the token-status update via its own SaveChangesAsync).
/// We then look up the session by <see cref="Civiti.Domain.Entities.McpSession.OpenIddictTokenId"/>
/// and stamp <c>RevokedAt</c> + reason. v1b.3 closes the linkage by populating
/// <see cref="Civiti.Domain.Entities.McpSession.OpenIddictTokenId"/> on every signin (see
/// <see cref="McpSessionWriteHandler"/>); rows minted before that landed have a null link and
/// are picked up later by the role-revalidation sweep instead.
/// </summary>
public sealed class McpSessionRevokeHandler(
    CivitiDbContext dbContext,
    ILogger<McpSessionRevokeHandler> logger)
    : IOpenIddictServerHandler<HandleRevocationRequestContext>
{
    public async ValueTask HandleAsync(HandleRevocationRequestContext context)
    {
        // GenericTokenPrincipal is populated by OpenIddict's <c>AttachPrincipal</c> handler
        // during the revocation pipeline once the token has been resolved from the store.
        // It's null if validation rejected the request earlier (unknown token, malformed
        // request, etc.) — we treat that as "nothing to mirror" and bail.
        var tokenId = context.GenericTokenPrincipal?.GetTokenId();
        if (string.IsNullOrEmpty(tokenId)) return;

        var session = await dbContext.McpSessions
            .FirstOrDefaultAsync(s => s.OpenIddictTokenId == tokenId && s.RevokedAt == null,
                context.CancellationToken);
        if (session is null)
        {
            // Either the session was minted pre-v1b.3 (OpenIddictTokenId == null) or the user
            // is revoking a refresh token Civiti.Auth never issued. The role sweep will catch
            // the first case; the second is a no-op.
            return;
        }

        session.RevokedAt = DateTime.UtcNow;
        session.RevokedReason = "user_revoked";
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("McpSession {SessionId} marked revoked via /revoke (tokenId {TokenId})",
            session.Id, tokenId);
    }
}
