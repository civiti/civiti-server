namespace Civiti.Domain.Entities;

/// <summary>
/// View-model table for the "Connected AI Assistants" UI. OpenIddict's token tables remain
/// the source of truth for token state; McpSessions lets us list active connections per user
/// without joining into OpenIddict storage on every page render.
///
/// Lifecycle is synced from three event points (see auth-design.md §5):
///   1. Token rotation → repoint OpenIddictTokenId to the newly-issued refresh-token entry,
///      bump LastSeenAt.
///   2. Explicit revocation (user-initiated or admin kill-switch) → set RevokedAt, RevokedReason.
///   3. Natural 30-day refresh-token expiry → background sweep sets RevokedAt with reason "expired".
/// </summary>
public class McpSession
{
    public Guid Id { get; set; }

    /// <summary>
    /// Current OpenIddict refresh-token entry backing this session. Repointed on rotation.
    /// Nullable during brief windows between token-consumed and new-token-issued.
    /// </summary>
    public string? OpenIddictTokenId { get; set; }

    /// <summary>
    /// OpenIddict client_id (e.g., "claude-desktop"). Matches the allow-list seed in
    /// auth-design.md §6.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Supabase user id (sub claim). Matches UserProfile.SupabaseUserId — stored as string,
    /// not uuid, to track the UserProfile schema already in place.
    /// </summary>
    public string SupabaseUserId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scopes granted at consent time (e.g., ["civiti.read", "civiti.write"]).
    /// Stored as a Postgres text[].
    /// </summary>
    public List<string> ScopesGranted { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Null while the session is active. Set on explicit revoke or natural expiry.
    /// Filter with <c>WHERE RevokedAt IS NULL</c> for the active-sessions query.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Free-form audit label ("user_revoked", "admin_killswitch", "expired",
    /// "role_lost", …). Null iff <see cref="RevokedAt"/> is null.
    /// </summary>
    public string? RevokedReason { get; set; }
}
