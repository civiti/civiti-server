using System.Text.Json;
using OpenIddict.Abstractions;

namespace Civiti.Auth.Endpoints;

/// <summary>
/// Gates <c>civiti.admin.*</c> scopes on (a) the OpenIddict client's <c>AllowsAdminScopes</c>
/// flag from the allow-list seed, (b) the user's Supabase role being <c>admin</c>. Both have
/// to be true; either being false strips admin scopes from the principal before SignIn.
/// auth-design.md §6.
/// </summary>
public sealed class AdminScopeFilter(
    IOpenIddictApplicationManager applicationManager,
    ILogger<AdminScopeFilter> logger)
{
    private const string AllowsAdminScopesProperty = "civiti.allows_admin_scopes";
    private const string AdminRole = "admin";

    private static readonly HashSet<string> AdminScopes = new(StringComparer.Ordinal)
    {
        "civiti.admin.read",
        "civiti.admin.write"
    };

    public async Task<FilterResult> FilterAsync(
        string clientId,
        string? userRole,
        IEnumerable<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        var requested = requestedScopes.ToList();
        var adminRequested = requested.Where(AdminScopes.Contains).ToList();

        if (adminRequested.Count == 0)
        {
            return new FilterResult(requested, []);
        }

        var clientAllows = await ClientAllowsAdminAsync(clientId, cancellationToken);
        var userIsAdmin = string.Equals(userRole, AdminRole, StringComparison.Ordinal);

        if (clientAllows && userIsAdmin)
        {
            return new FilterResult(requested, []);
        }

        var allowed = requested.Where(s => !AdminScopes.Contains(s)).ToList();
        logger.LogInformation(
            "Admin scopes stripped for client {ClientId}, role {Role}, clientAllows={ClientAllows}: stripped={Stripped}",
            clientId, userRole ?? "(none)", clientAllows, string.Join(',', adminRequested));
        return new FilterResult(allowed, adminRequested);
    }

    private async Task<bool> ClientAllowsAdminAsync(string clientId, CancellationToken cancellationToken)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return false;
        }

        var properties = await applicationManager.GetPropertiesAsync(application, cancellationToken);
        return properties.TryGetValue(AllowsAdminScopesProperty, out var element)
               && element.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Result of <see cref="FilterAsync"/>. <see cref="Allowed"/> is the scope list to grant;
    /// <see cref="Stripped"/> lists scopes the user requested but couldn't have, useful for the
    /// consent screen to render "We removed: civiti.admin.read because your account isn't an
    /// admin." messaging.
    /// </summary>
    public sealed record FilterResult(IReadOnlyList<string> Allowed, IReadOnlyList<string> Stripped);
}
