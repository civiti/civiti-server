namespace Civiti.Api.Infrastructure.Configuration;

/// <summary>
/// Configuration for the admin-on-new-issue email pipeline.
/// </summary>
public class AdminNotifyConfiguration
{
    /// <summary>
    /// Feature flag. When false, new-issue submissions are NOT announced to admins.
    /// Intended to be false in dev, true in prod.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// In-memory cache TTL (seconds) for the admin email list. A brief cache
    /// absorbs bursts of issue submissions without hammering the Supabase Admin API.
    /// </summary>
    public int AdminListCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Bounded channel capacity for admin-notify requests. Drop-write under pressure
    /// (we'd rather skip a notification than block issue creation).
    /// </summary>
    public int ChannelCapacity { get; set; } = 1_000;

    /// <summary>
    /// Max retry attempts when calling the Supabase Admin API on transient (5xx / network) failure.
    /// </summary>
    public int MaxSupabaseRetries { get; set; } = 3;

    /// <summary>
    /// Timeout (seconds) for a single Supabase Admin API request.
    /// </summary>
    public int SupabaseTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Page size when listing users from the Supabase Admin API. Upper bound
    /// is what Supabase accepts (1000 at time of writing); we keep it modest
    /// to limit per-request memory and latency.
    /// </summary>
    public int SupabasePageSize { get; set; } = 200;

    /// <summary>
    /// Hard cap on pages we'll walk when listing admins. Defensive guard against
    /// pathological pagination loops. At the default page size of 200 this covers
    /// 10k users, well above any realistic admin cohort.
    /// </summary>
    public int MaxSupabasePages { get; set; } = 50;
}
