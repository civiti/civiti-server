using System.ComponentModel;
using Civiti.Application.Services;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §1 Public leaderboard tool. See Civiti.Mcp/docs/tool-inventory.md §1.
/// City-scoped leaderboards are deferred — see §8 open question #4.
/// </summary>
[McpServerToolType]
public sealed class PublicGamificationTools(IGamificationService gamification)
{
    [McpServerTool(Name = "get_leaderboard")]
    [Description("Return the top contributors by points or another category. Mirrors GET /api/gamification/leaderboard.")]
    public async Task<object> GetLeaderboard(
        [Description("Time window: all | month | week. Default \"all\".")] string? period = null,
        [Description("Ranking category: points | issues | votes. Default \"points\".")] string? category = null,
        [Description("Number of rows to return, 1–50. Default 50.")] int? limit = null)
    {
        var result = await gamification.GetLeaderboardAsync(
            period: string.IsNullOrWhiteSpace(period) ? "all" : period,
            category: string.IsNullOrWhiteSpace(category) ? "points" : category,
            limit: Math.Clamp(limit ?? 50, 1, 50));
        return result;
    }
}
