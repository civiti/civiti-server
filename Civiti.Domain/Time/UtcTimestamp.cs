namespace Civiti.Domain.Time;

/// <summary>
/// UTC timestamps truncated to microsecond precision — the resolution PostgreSQL's
/// <c>timestamp</c> type actually stores.
/// <para>
/// .NET's <see cref="DateTime"/> carries 100-nanosecond ticks, so a value stamped in memory
/// and handed straight back in a response is not equal to the value that later comes out of
/// the database. That difference is invisible until a timestamp is used as an
/// optimistic-concurrency token: the client echoes back the sub-microsecond value it was
/// given, it never matches the stored one, and every second consecutive edit fails with a
/// spurious conflict.
/// </para>
/// </summary>
public static class UtcTimestamp
{
    /// <summary>Current UTC time, truncated so it survives a database round-trip unchanged.</summary>
    public static DateTime Now() => Truncate(DateTime.UtcNow);

    /// <summary>Drops sub-microsecond ticks, preserving <see cref="DateTime.Kind"/>.</summary>
    public static DateTime Truncate(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, value.Kind);

    /// <summary>
    /// Normalises a client-supplied timestamp for comparison against a stored one: converts to
    /// UTC and truncates.
    /// <para>
    /// A value that arrives without an offset is read as UTC rather than as local time — the API
    /// deals only in UTC, and interpreting it in the server's zone would silently shift it.
    /// </para>
    /// </summary>
    public static DateTime NormalizeToUtc(DateTime value) => Truncate(
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime());
}
