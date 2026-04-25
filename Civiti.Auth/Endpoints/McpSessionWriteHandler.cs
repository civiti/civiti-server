using Civiti.Domain.Entities;
using Civiti.Infrastructure.Data;
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
internal sealed class McpSessionWriteHandler(
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
        if (!context.Request.IsAuthorizationCodeGrantType())
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

        var session = new McpSession
        {
            Id = Guid.NewGuid(),
            ClientId = context.Request.ClientId ?? string.Empty,
            SupabaseUserId = supabaseUserId,
            ScopesGranted = principal.GetScopes().ToList(),
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        dbContext.McpSessions.Add(session);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("McpSession written: {SessionId}, sub {Sub}, client {ClientId}",
            session.Id, supabaseUserId, context.Request.ClientId);
    }
}
