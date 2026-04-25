namespace Civiti.Application.Services;

/// <summary>
/// Thin wrapper around the Supabase Admin Users API, scoped to what this backend
/// needs (listing admins). Isolates the HTTP dependency so callers can be unit-tested.
///
/// Admin identity is determined by <c>raw_app_meta_data.role == "admin"</c> on
/// <c>auth.users</c>. Admin status is granted manually via SQL today; this method
/// never writes — it only reads.
/// </summary>
public interface ISupabaseAdminClient
{
    /// <summary>
    /// Returns every user with <c>app_metadata.role == "admin"</c>.
    /// The result is briefly cached in memory (see AdminNotifyConfiguration.AdminListCacheSeconds).
    /// Retries transient 5xx / network failures with exponential backoff.
    /// </summary>
    Task<IReadOnlyList<SupabaseAdminUser>> ListAdminsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single Supabase user by their <c>sub</c> (auth.users.id). Returns
    /// <c>null</c> when the user no longer exists — callers MUST treat that as a
    /// revocation signal (auth-design.md §4: every refresh re-validates the upstream
    /// user; a missing user means the session must be revoked). Used by Civiti.Auth's
    /// refresh-token handler and the admin-revalidation sweep.
    /// </summary>
    Task<SupabaseUserSnapshot?> GetUserAsync(string supabaseUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Subset of a Supabase auth.users record that we care about for notification delivery.
/// </summary>
public record SupabaseAdminUser(Guid Id, string Email);

/// <summary>
/// Single-user snapshot returned by <see cref="ISupabaseAdminClient.GetUserAsync"/>.
/// Role mirrors <c>app_metadata.role</c>; null/empty for ordinary citizens. Banned/disabled
/// state is surfaced via <see cref="BannedUntilUtc"/> being non-null AND in the future —
/// we treat that as "user is currently disabled" rather than re-implementing Supabase's
/// banning rules.
/// </summary>
public record SupabaseUserSnapshot(Guid Id, string? Email, string? Role, DateTime? BannedUntilUtc);
