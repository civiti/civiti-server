using Civiti.Domain.Entities;
using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// Writes the <see cref="McpSession"/> audit row from inside OpenIddict's signin pipeline so it
/// commits on the same path as the issued tokens. v1b.3 also captures the issued refresh
/// token's database id from <see cref="ProcessSignInContext.RefreshTokenPrincipal"/> and writes
/// it to <see cref="McpSession.OpenIddictTokenId"/>, closing the linkage that v1b.1/.2 left
/// nullable. The id is the prerequisite for the /revoke handler in <see cref="McpSessionRevokeHandler"/>
/// and the role-revalidation sweep in <see cref="Civiti.Infrastructure.Services.Auth.McpSessionRoleRevalidationSweep"/>:
/// both use it to tie an OpenIddict token revoke back to its session row.
/// </summary>
public sealed class McpSessionWriteHandler(
    CivitiDbContext dbContext,
    ILogger<McpSessionWriteHandler> logger)
    : IOpenIddictServerHandler<ProcessSignInContext>
{
    public async ValueTask HandleAsync(ProcessSignInContext context)
    {
        if (context.EndpointType != OpenIddictServerEndpointType.Token)
        {
            return;
        }

        var isCodeGrant = context.Request.IsAuthorizationCodeGrantType();
        var isRefresh = context.Request.IsRefreshTokenGrantType();
        if (!isCodeGrant && !isRefresh)
        {
            return;
        }

        var principal = context.Principal;
        if (principal is null)
        {
            return;
        }

        var supabaseUserId = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (string.IsNullOrEmpty(supabaseUserId))
        {
            logger.LogWarning("ProcessSignIn: principal has no sub claim — skipping McpSession write");
            return;
        }

        var clientId = context.Request.ClientId ?? string.Empty;
        var scopes = principal.GetScopes().ToList();

        // RefreshTokenPrincipal carries the to-be-issued refresh token's claims, including the
        // OpenIddict store id under the private oi_tkn_id claim. If we're not minting a refresh
        // token (e.g. the request didn't include offline_access on the original /authorize), the
        // principal is null and we leave OpenIddictTokenId null — the entity comment documents
        // that as an acceptable transient state, and v1b.3's revoke + sweep handlers tolerate it.
        var refreshTokenId = context.RefreshTokenPrincipal?.GetTokenId();

        // McpSession is a (user, client) view-model row — find the existing active one and
        // update it on refresh, otherwise insert a new row on first code-grant. If the user
        // somehow re-authenticates fresh while an old row is still active (e.g. cookie
        // session bypass, edge case), we update in place rather than creating a duplicate
        // surfacing in the "Connected AI Assistants" UI.
        var existing = await dbContext.McpSessions
            .Where(s => s.SupabaseUserId == supabaseUserId
                        && s.ClientId == clientId
                        && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (existing is null)
        {
            var session = new McpSession
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                SupabaseUserId = supabaseUserId,
                ScopesGranted = scopes,
                OpenIddictTokenId = refreshTokenId,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            dbContext.McpSessions.Add(session);
            logger.LogInformation("McpSession created: {SessionId}, sub {Sub}, client {ClientId}, tokenId {TokenId} ({Grant})",
                session.Id, supabaseUserId, clientId, refreshTokenId ?? "(none)", context.Request.GrantType);
        }
        else
        {
            existing.LastSeenAt = DateTime.UtcNow;
            // Track the latest scope set in case the user re-consented to a different scope
            // shape on a fresh /authorize that landed back on this row.
            existing.ScopesGranted = scopes;
            // Repoint to the rotated refresh token's id so /revoke and the role-revalidation
            // sweep can find this session by token id without a stale-row chase. If the new
            // signin doesn't mint a refresh token, leave the existing id alone.
            if (!string.IsNullOrEmpty(refreshTokenId))
            {
                existing.OpenIddictTokenId = refreshTokenId;
            }
            logger.LogInformation("McpSession refreshed: {SessionId}, sub {Sub}, client {ClientId}, tokenId {TokenId}",
                existing.Id, supabaseUserId, clientId, refreshTokenId ?? "(unchanged)");
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
