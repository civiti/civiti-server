namespace Civiti.Mcp.Authorization;

/// <summary>
/// The Supabase identity behind an authenticated MCP request — what
/// <see cref="IMcpCitizenContext.RequireCitizenReadAsync"/> returns.
/// Use this when the backing service signature takes <c>string supabaseUserId</c>
/// (currently UserService, IssueService, BlockService).
/// </summary>
/// <param name="SupabaseUserId">JWT <c>sub</c> claim.</param>
public sealed record CitizenContext(string SupabaseUserId);

/// <summary>
/// Same as <see cref="CitizenContext"/> plus Civiti's internal <c>UserProfile.Id</c>, populated
/// by <see cref="IMcpCitizenContext.ResolveCitizenAsync"/>. Use this when the backing service
/// signature takes <c>Guid userId</c> (currently ActivityService).
/// Splitting into two record types — instead of one with a nullable Guid — lets the type
/// system enforce that <c>InternalUserId</c> is present at the call site without an
/// <c>!.Value</c> dance.
/// </summary>
/// <param name="SupabaseUserId">JWT <c>sub</c> claim.</param>
/// <param name="InternalUserId">Civiti <c>UserProfile.Id</c>, resolved via the user store.</param>
public sealed record IdentifiedCitizenContext(string SupabaseUserId, Guid InternalUserId);
