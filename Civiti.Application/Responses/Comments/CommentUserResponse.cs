using Civiti.Domain.Attributes;

namespace Civiti.Application.Responses.Comments;

/// <summary>
/// Nested user information for comment responses
/// </summary>
public class CommentUserResponse
{
    /// <summary>
    /// The comment author's Supabase auth id (the JWT <c>sub</c>) — the identifier a client can
    /// compare against its own to decide ownership. Empty for a deleted author. Not the internal
    /// <c>UserProfile.Id</c> PK.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    [Untrusted] public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public int Level { get; set; }
}
