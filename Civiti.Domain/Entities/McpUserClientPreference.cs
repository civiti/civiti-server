namespace Civiti.Domain.Entities;

/// <summary>
/// Persists a user's "remember this client" consent decision so future /authorize hits from
/// the same client+scopes can skip the Razor consent page. One row per (SupabaseUserId,
/// ClientId) — re-consent is only required if the client requests scopes that aren't covered
/// by <see cref="ScopesGranted"/>. Auth-design.md §7: optional but desirable UX so users
/// don't see the consent screen on every refresh-induced re-auth.
/// </summary>
public class McpUserClientPreference
{
    public Guid Id { get; set; }

    /// <summary>
    /// Supabase user id (sub claim). Mirrors <see cref="UserProfile.SupabaseUserId"/>.
    /// </summary>
    public string SupabaseUserId { get; set; } = string.Empty;

    /// <summary>
    /// OpenIddict client_id from the allow-list (<c>claude-desktop</c>, <c>claude-ai</c>, …).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scopes the user explicitly approved at consent time. Stored as a Postgres text[].
    /// A new /authorize request is auto-approved iff its requested scopes are a subset.
    /// </summary>
    public List<string> ScopesGranted { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bumped whenever the user re-consents (e.g. the client now wants a wider scope set).
    /// Useful for the future "Connected AI Assistants" UI to show last-consented timestamps
    /// alongside the active <see cref="McpSession"/> rows.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
