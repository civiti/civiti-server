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
    Task<CitizenAuthResult<CitizenContext>> RequireCitizenReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the principal is authenticated and carries <c>civiti.read</c>, then resolves
    /// the internal <c>UserProfile.Id</c> via <c>IUserService.GetUserIdAsync</c>. Use when a
    /// downstream service signature takes <c>Guid</c> instead of <c>string supabaseUserId</c>
    /// (currently <c>IActivityService.GetUserActivitiesAsync</c>).
    /// </summary>
    Task<CitizenAuthResult<IdentifiedCitizenContext>> ResolveCitizenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the principal is authenticated and carries <c>civiti.write</c>. Per
    /// <c>auth-design.md §8</c> scopes are independent — a write-only token can call write
    /// tools without also holding <c>civiti.read</c>. Returns the Supabase <c>sub</c>; does
    /// NOT hit the DB to resolve the internal Guid.
    /// </summary>
    Task<CitizenAuthResult<CitizenContext>> RequireCitizenWriteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the principal is authenticated and carries <c>civiti.write</c>, then resolves
    /// the internal <c>UserProfile.Id</c>. Currently no §2.2 write tool needs the internal
    /// Guid (every backing service for the v1c write surface accepts <c>string supabaseUserId</c>),
    /// but kept symmetric with <see cref="ResolveCitizenAsync"/> so future write tools whose
    /// service signatures change don't have to touch the helper.
    /// </summary>
    Task<CitizenAuthResult<IdentifiedCitizenContext>> ResolveCitizenWriteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort identity resolution for tools that work in both anonymous and authenticated
    /// modes — currently <c>search_issues</c> and <c>get_issue</c>, registered on both
    /// <c>/mcp</c> and <c>/mcp/public</c>. Returns the internal <c>UserProfile.Id</c> when the
    /// principal is authenticated, carries a <c>sub</c> claim, and a matching profile exists;
    /// otherwise <c>null</c> (anonymous mount, missing sub, profile not provisioned).
    ///
    /// <para>
    /// Deliberately does NOT enforce a scope — this helper is for personalization
    /// (block-list filtering, <c>HasVoted</c> enrichment), not authorization. Returning
    /// <c>null</c> is a clean degrade: the downstream service treats it as anonymous and
    /// returns the same data set the public mount sees today.
    /// </para>
    /// </summary>
    Task<Guid?> TryResolveCitizenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Discriminated result: either <c>Context</c> is non-null (authorized) or <c>ErrorPayload</c>
/// is non-null (rejected — the tool returns the payload as its result).
/// Generic over the context shape so <c>RequireCitizenReadAsync</c> and
/// <c>ResolveCitizenAsync</c> can return precisely-typed contexts (sub-only vs. with internal
/// Guid) and tools can dereference fields without nullability gymnastics.
/// </summary>
public sealed record CitizenAuthResult<TContext>(TContext? Context, object? ErrorPayload)
    where TContext : class
{
    [MemberNotNullWhen(true, nameof(Context))]
    [MemberNotNullWhen(false, nameof(ErrorPayload))]
    public bool Authorized => Context is not null;

    public static CitizenAuthResult<TContext> FromContext(TContext context) => new(context, null);

    public static CitizenAuthResult<TContext> Rejected(string reason, string message) =>
        new(null, new { ok = false, reason, message });
}
