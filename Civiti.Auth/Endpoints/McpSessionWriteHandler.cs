using Civiti.Domain.Entities;
using Civiti.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// Writes the <see cref="McpSession"/> audit row from inside OpenIddict's signin pipeline so it
/// commits on the same path as the issued tokens. v1b.1's first cut wrote the row from the
/// /token endpoint <em>before</em> <see cref="IResult"/> SignIn returned, which left an orphan
/// row whenever OpenIddict's later token-creation handlers threw (the row would surface in the
/// "Connected AI Assistants" UI as a phantom session). Registering as an
/// <see cref="IOpenIddictServerHandler{TContext}"/> moves the write into the same request path
/// that issues the tokens — if the SignIn pipeline aborts before this handler runs, no row is
/// persisted.
///
/// Filters: token endpoint only, authorization_code grant only. /authorize signin events also
/// fire <see cref="ProcessSignInContext"/> but those issue auth codes, not refresh tokens —
/// nothing to track in McpSessions for that path. Refresh-token rotation lands in v1b.2 with a
/// separate handler that re-points <see cref="McpSession.OpenIddictTokenId"/> on each rotation.
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
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            dbContext.McpSessions.Add(session);
            logger.LogInformation("McpSession created: {SessionId}, sub {Sub}, client {ClientId} ({Grant})",
                session.Id, supabaseUserId, clientId, context.Request.GrantType);
        }
        else
        {
            existing.LastSeenAt = DateTime.UtcNow;
            // Track the latest scope set in case the user re-consented to a different scope
            // shape on a fresh /authorize that landed back on this row.
            existing.ScopesGranted = scopes;
            logger.LogInformation("McpSession refreshed: {SessionId}, sub {Sub}, client {ClientId}",
                existing.Id, supabaseUserId, clientId);
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
