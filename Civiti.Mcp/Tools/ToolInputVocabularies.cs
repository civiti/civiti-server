namespace Civiti.Mcp.Tools;

/// <summary>
/// Tool-parameter whitelists shared across §2.2 write tools. Centralised here so a new
/// allowed value (e.g. a "downvote" direction) lands in one place instead of multiple
/// per-tool copies that would inevitably drift.
/// </summary>
internal static class ToolInputVocabularies
{
    /// <summary>
    /// Accepted values for <c>vote_on_issue</c> and <c>vote_on_comment</c>'s
    /// <c>direction</c> parameter. <c>"up"</c> dispatches to the service's "vote/mark
    /// helpful" path; <c>"remove"</c> dispatches to the service's vote-retraction path.
    /// </summary>
    public static readonly HashSet<string> VoteDirections =
        new(["up", "remove"], StringComparer.OrdinalIgnoreCase);
}
