namespace Civiti.Mcp.Authorization;

/// <summary>
/// Resolved citizen identity for the current MCP tool invocation.
/// </summary>
/// <param name="SupabaseUserId">JWT <c>sub</c> claim — the Supabase user id, used by services
/// that key off the upstream identity (UserService, IssueService, BlockService).</param>
/// <param name="InternalUserId">Civiti's internal <c>UserProfile.Id</c>. Resolved lazily; only
/// populated when the tool asked for it via <see cref="IMcpCitizenContext.ResolveCitizenAsync"/>.
/// Null when the tool only called <see cref="IMcpCitizenContext.RequireCitizenReadAsync"/>.</param>
public sealed record CitizenContext(string SupabaseUserId, Guid? InternalUserId = null);
