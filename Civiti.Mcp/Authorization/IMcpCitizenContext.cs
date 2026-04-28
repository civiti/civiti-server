using System.Diagnostics.CodeAnalysis;

namespace Civiti.Mcp.Authorization;

/// <summary>
/// Single chokepoint every authenticated MCP tool calls before doing any work. Reads the
/// validated principal off <c>HttpContextAccessor</c>, enforces a required scope, extracts the
/// Supabase <c>sub</c>, and (optionally) resolves the internal <c>UserProfile.Id</c>.
///
/// Returns a structured failure result rather than throwing so tools can surface
/// <c>{ok: false, reason: ..., message: ...}</c> payloads — the same shape <c>tool-inventory.md
/// §2.3</c> spec'd for moderation rejections, so MCP clients see a consistent error contract.
/// </summary>
public interface IMcpCitizenContext
{
    /// <summary>
    /// Verifies the principal is authenticated and carries <c>civiti.read</c>. Returns the
    /// Supabase <c>sub</c>; does NOT hit the DB to resolve the internal Guid (skip the
    /// extra round-trip when a tool only needs the upstream identity).
    /// </summary>
    Task<CitizenAuthResult> RequireCitizenReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the principal is authenticated and carries <c>civiti.read</c>, then resolves
    /// the internal <c>UserProfile.Id</c> via <c>IUserService.GetUserIdAsync</c>. Use when a
    /// downstream service signature takes <c>Guid</c> instead of <c>string supabaseUserId</c>
    /// (currently <c>IActivityService.GetUserActivitiesAsync</c>, <c>IGamificationService</c>'s
    /// per-user methods).
    /// </summary>
    Task<CitizenAuthResult> ResolveCitizenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Discriminated result: either <c>Context</c> is non-null (authorized) or <c>ErrorPayload</c>
/// is non-null (rejected — the tool returns the payload as its result).
/// </summary>
public sealed record CitizenAuthResult(CitizenContext? Context, object? ErrorPayload)
{
    [MemberNotNullWhen(true, nameof(Context))]
    [MemberNotNullWhen(false, nameof(ErrorPayload))]
    public bool Authorized => Context is not null;

    public static CitizenAuthResult FromContext(CitizenContext context) => new(context, null);

    public static CitizenAuthResult Rejected(string reason, string message) =>
        new(null, new { ok = false, reason, message });
}
