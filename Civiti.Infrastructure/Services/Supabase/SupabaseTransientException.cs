namespace Civiti.Infrastructure.Services.Supabase;

/// <summary>
/// Thrown by <see cref="ISupabaseAdminClient"/> when an admin-API call returns a non-2xx,
/// non-404 status (5xx, 429, etc). Distinct from a <c>null</c> return so callers can tell
/// "the user is genuinely missing" from "we can't reach Supabase right now". The
/// <see cref="Civiti.Application.Services.ISupabaseAdminClient"/> contract:
/// <list type="bullet">
///   <item><description>returns a snapshot when the user exists,</description></item>
///   <item><description>returns <c>null</c> on 404 or missing service-role key (legitimate "no record"),</description></item>
///   <item><description>throws this exception on every other failure mode.</description></item>
/// </list>
/// Sweep code that catches and skips on this avoids permanently revoking active sessions
/// during a transient Supabase outage; refresh-grant code that catches and denies preserves
/// fail-closed semantics during the same outage.
/// </summary>
public sealed class SupabaseTransientException(int statusCode, string supabaseUserId)
    : Exception($"Supabase admin API returned HTTP {statusCode} when looking up user {supabaseUserId}")
{
    public int StatusCode { get; } = statusCode;
}
