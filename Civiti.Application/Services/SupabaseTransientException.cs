namespace Civiti.Application.Services;

/// <summary>
/// Thrown by <see cref="ISupabaseAdminClient"/> when an admin-API call cannot deliver a
/// trustworthy answer — non-2xx HTTP (5xx, 429, etc.) or a 2xx with an unparseable body
/// (a WAF/CDN HTML response, schema drift, missing required fields). Distinct from a
/// <c>null</c> return so callers can tell "the user is genuinely missing" from "we can't
/// trust what Supabase gave us right now".
///
/// <see cref="ISupabaseAdminClient"/> contract:
/// <list type="bullet">
///   <item><description>returns a snapshot when the user exists and the body parsed cleanly,</description></item>
///   <item><description>returns <c>null</c> on 404 or missing service-role key (legitimate "no record"),</description></item>
///   <item><description>throws this exception on every other failure mode.</description></item>
/// </list>
/// Sweep code that catches and skips on this avoids permanently revoking active sessions
/// during a transient Supabase outage; refresh-grant code that catches and denies preserves
/// fail-closed semantics during the same outage. Lives in the application layer so callers
/// don't have to reference the infrastructure assembly to react to the contract.
/// </summary>
public sealed class SupabaseTransientException(int statusCode, string supabaseUserId, string? reason = null)
    : Exception($"Supabase admin API failure (HTTP {statusCode}) for user {supabaseUserId}{(reason is null ? string.Empty : $": {reason}")}")
{
    public int StatusCode { get; } = statusCode;
}
