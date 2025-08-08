namespace Civica.Api.Models.Responses.Gamification;

public class LeaderboardResponse
{
    public List<LeaderboardEntry> Leaderboard { get; set; } = [];
    public string Period { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int TotalEntries { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public UserInfo User { get; set; } = new();
    public int Points { get; set; }
    public int Level { get; set; }
    public int IssuesReported { get; set; }
    public int IssuesResolved { get; set; }
    public List<string> RecentBadges { get; set; } = [];
}

public class UserInfo
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string City { get; set; } = string.Empty;
}