namespace Civiti.Api.Services.Interfaces;

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
}

/// <summary>
/// Subset of a Supabase auth.users record that we care about for notification delivery.
/// </summary>
public record SupabaseAdminUser(Guid Id, string Email);
