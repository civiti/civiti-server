using Civiti.Api.Models.Domain;
using Civiti.Api.Models.Requests.Admin;
using Civiti.Api.Models.Requests.Auth;
using Civiti.Api.Models.Requests.Issues;
using Civiti.Api.Models.Responses.Activity;
using Civiti.Api.Models.Responses.Admin;
using Civiti.Api.Models.Responses.Auth;
using Civiti.Api.Models.Responses.Authority;
using Civiti.Api.Models.Responses.Comments;
using Civiti.Api.Models.Responses.Common;
using Civiti.Api.Models.Responses.Gamification;
using Civiti.Api.Models.Responses.Issues;
using Civiti.Api.Models.Responses.User;
using Swashbuckle.AspNetCore.Filters;

namespace Civiti.Api.Infrastructure.Configuration;

/// <summary>
/// Example provider for CreateUserProfileRequest
/// </summary>
public class CreateUserProfileRequestExample : IExamplesProvider<CreateUserProfileRequest>
{
    public CreateUserProfileRequest GetExamples()
    {
        return new CreateUserProfileRequest
        {
            DisplayName = "Ion Popescu",
            PhotoUrl = "https://storage.civiti.ro/avatars/user-123.jpg",
            County = "București",
            City = "București",
            District = "Sector 1",
            ResidenceType = ResidenceType.Apartment
        };
    }
}

/// <summary>
/// Example provider for UserProfileResponse
/// </summary>
public class UserProfileResponseExample : IExamplesProvider<UserProfileResponse>
{
    public UserProfileResponse GetExamples()
    {
        return new UserProfileResponse
        {
            Id = Guid.NewGuid(),
            Email = "ion.popescu@example.com",
            DisplayName = "Ion Popescu",
            PhotoUrl = "https://storage.civiti.ro/avatars/user-123.jpg",
            County = "București",
            City = "București",
            District = "Sector 1",
            ResidenceType = "Apartment",
            Points = 150,
            Level = 2,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            EmailVerified = true,

            // Notification preferences
            IssueUpdatesEnabled = true,
            CommunityNewsEnabled = true,
            MonthlyDigestEnabled = false,
            AchievementsEnabled = true,

            // Gamification data (will be populated separately)
            Gamification = null
        };
    }
}

/// <summary>
/// Example provider for CreateIssueRequest
/// </summary>
public class CreateIssueRequestExample : IExamplesProvider<CreateIssueRequest>
{
    public CreateIssueRequest GetExamples()
    {
        return new CreateIssueRequest
        {
            Title = "Groapă periculoasă pe strada Mihai Eminescu",
            Description = "O groapă adâncă de aproximativ 50cm s-a format în asfalt, reprezentând un pericol pentru vehicule și pietoni.",
            Category = IssueCategory.Infrastructure,
            Urgency = UrgencyLevel.High,
            District = "Sector 2",
            Address = "Strada Mihai Eminescu, Nr. 45",
            Latitude = 44.4268,
            Longitude = 26.1025,
            PhotoUrls =
            [
                "https://storage.civiti.ro/photos/issue-123-photo1.jpg",
                "https://storage.civiti.ro/photos/issue-123-photo2.jpg"
            ],
            Authorities =
            [
                new IssueAuthorityInput { AuthorityId = Guid.NewGuid() },
                new IssueAuthorityInput { CustomName = "Primăria Sector 2", CustomEmail = "primarie.sector2@pmb.ro" }
            ]
        };
    }
}

/// <summary>
/// Example provider for IssueListResponse
/// </summary>
public class IssueListResponseExample : IExamplesProvider<IssueListResponse>
{
    public IssueListResponse GetExamples()
    {
        return new IssueListResponse
        {
            Id = Guid.NewGuid(),
            Title = "Groapă periculoasă pe strada Mihai Eminescu",
            Description = "O groapă adâncă de aproximativ 50cm s-a format în asfalt...",
            Category = IssueCategory.Infrastructure,
            Address = "Strada Mihai Eminescu, Nr. 45",
            Urgency = UrgencyLevel.High,
            EmailsSent = 245,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            MainPhotoUrl = "https://storage.civiti.ro/photos/issue-123-photo1.jpg",
            Status = IssueStatus.Active
        };
    }
}

/// <summary>
/// Example provider for PagedResult of IssueListResponse
/// </summary>
public class PagedIssueListResponseExample : IExamplesProvider<PagedResult<IssueListResponse>>
{
    public PagedResult<IssueListResponse> GetExamples()
    {
        IssueListResponse issueExample = new IssueListResponseExample().GetExamples();

        return new PagedResult<IssueListResponse>
        {
            Items = [issueExample],
            Page = 1,
            PageSize = 12,
            TotalPages = 4
        };
    }
}

/// <summary>
/// Example provider for AdminIssueResponse (lightweight list view)
/// </summary>
public class AdminIssueResponseExample : IExamplesProvider<AdminIssueResponse>
{
    public AdminIssueResponse GetExamples()
    {
        return new AdminIssueResponse
        {
            Id = Guid.NewGuid(),
            Title = "Iluminat stradal defect",
            Category = IssueCategory.Infrastructure,
            Urgency = UrgencyLevel.Medium,
            Status = IssueStatus.Submitted,
            Address = "Strada Victoriei, între nr. 10-50",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            PhotoCount = 3,
            EmailsSent = 0,
            UserName = "Maria Ionescu"
        };
    }
}

/// <summary>
/// Example provider for ApproveIssueRequest
/// </summary>
public class ApproveIssueRequestExample : IExamplesProvider<ApproveIssueRequest>
{
    public ApproveIssueRequest GetExamples()
    {
        return new ApproveIssueRequest
        {
            AdminNotes = "Issue verified and approved for public visibility. High priority due to safety concerns."
        };
    }
}

/// <summary>
/// Example provider for RejectIssueRequest
/// </summary>
public class RejectIssueRequestExample : IExamplesProvider<RejectIssueRequest>
{
    public RejectIssueRequest GetExamples()
    {
        return new RejectIssueRequest
        {
            Reason = "Duplicate of existing issue #123. Please support the existing campaign instead."
        };
    }
}

/// <summary>
/// Example provider for UserGamificationResponse
/// </summary>
public class UserGamificationResponseExample : IExamplesProvider<UserGamificationResponse>
{
    public UserGamificationResponse GetExamples()
    {
        return new UserGamificationResponse
        {
            Points = 350,
            Level = 3,
            IssuesReported = 12,
            IssuesResolved = 5,
            CommunityVotes = 45,
            CurrentLoginStreak = 7,
            LongestLoginStreak = 15,
            CurrentLevelPoints = 300,
            NextLevelPoints = 500,
            PointsToNextLevel = 150,
            PointsInCurrentLevel = 50,
            LevelProgressPercentage = 25.0,
            RecentBadges =
            [
                new BadgeResponse
                {
                    Id = Guid.NewGuid(),
                    Name = "First Issue",
                    Description = "Created your first issue",
                    IconUrl = "https://storage.civiti.ro/badges/first-issue.svg",
                    Category = "Participation",
                    Rarity = "Common",
                    RequirementDescription = "Report your first civic issue",
                    EarnedAt = DateTime.UtcNow.AddDays(-30),
                    IsEarned = true
                },

                new BadgeResponse
                {
                    Id = Guid.NewGuid(),
                    Name = "Email Warrior",
                    Description = "Sent 100 emails for civic causes",
                    IconUrl = "https://storage.civiti.ro/badges/email-warrior.svg",
                    Category = "Engagement",
                    Rarity = "Rare",
                    RequirementDescription = "Send 100 emails through campaigns",
                    EarnedAt = DateTime.UtcNow.AddDays(-7),
                    IsEarned = true
                }
            ],
            ActiveAchievements =
            [
                new AchievementProgressResponse
                {
                    Id = Guid.NewGuid(),
                    Title = "Campaign Starter",
                    Description = "Start 10 email campaigns",
                    Progress = 7,
                    MaxProgress = 10,
                    RewardPoints = 50,
                    Completed = false,
                    CompletedAt = null,
                    PercentageComplete = 70.0m
                }
            ]
        };
    }
}

/// <summary>
/// Example provider for LeaderboardResponse
/// </summary>
public class LeaderboardResponseExample : IExamplesProvider<LeaderboardResponse>
{
    public LeaderboardResponse GetExamples()
    {
        return new LeaderboardResponse
        {
            Leaderboard =
            [
                new LeaderboardEntry
                {
                    Rank = 1,
                    User = new UserInfo
                    {
                        Id = Guid.NewGuid(),
                        DisplayName = "Ana Popa",
                        PhotoUrl = "https://storage.civiti.ro/avatars/user-456.jpg",
                        City = "București"
                    },
                    Points = 1250,
                    Level = 5,
                    IssuesReported = 32,
                    IssuesResolved = 18,
                    RecentBadges = ["Email Warrior", "Community Hero", "Problem Solver"]
                },

                new LeaderboardEntry
                {
                    Rank = 2,
                    User = new UserInfo
                    {
                        Id = Guid.NewGuid(),
                        DisplayName = "Mihai Ionescu",
                        PhotoUrl = "https://storage.civiti.ro/avatars/user-789.jpg",
                        City = "Cluj-Napoca"
                    },
                    Points = 1180,
                    Level = 5,
                    IssuesReported = 28,
                    IssuesResolved = 15,
                    RecentBadges = ["Active Citizen", "Weekly Champion"]
                },

                new LeaderboardEntry
                {
                    Rank = 3,
                    User = new UserInfo
                    {
                        Id = Guid.NewGuid(),
                        DisplayName = "Elena Dumitrescu",
                        PhotoUrl = null,
                        City = "Timișoara"
                    },
                    Points = 950,
                    Level = 4,
                    IssuesReported = 20,
                    IssuesResolved = 12,
                    RecentBadges = ["Rising Star"]
                }
            ],
            Period = "monthly",
            Category = "points",
            TotalEntries = 125,
            GeneratedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Example provider for AdminStatisticsResponse
/// </summary>
public class AdminStatisticsResponseExample : IExamplesProvider<AdminStatisticsResponse>
{
    public AdminStatisticsResponse GetExamples()
    {
        return new AdminStatisticsResponse
        {
            // Issue statistics
            TotalSubmissions = 156,
            PendingReview = 12,
            Approved = 120,
            Rejected = 24,
            Active = 45,
            Resolved = 38,
            Cancelled = 8,

            // Time-based statistics
            SubmissionsToday = 3,
            SubmissionsThisWeek = 18,
            SubmissionsThisMonth = 42,

            // Admin activity
            ReviewedToday = 5,
            ReviewedThisWeek = 28,
            ReviewedThisMonth = 95,
            AverageReviewTimeHours = 4.5,

            // Category breakdown
            IssuesByCategory = new Dictionary<string, int>
            {
                ["Infrastructure"] = 45,
                ["StreetLighting"] = 32,
                ["GreenSpaces"] = 28,
                ["PublicTransport"] = 25,
                ["WasteManagement"] = 26
            },
            IssuesByUrgency = new Dictionary<string, int>
            {
                ["Low"] = 28,
                ["Medium"] = 65,
                ["High"] = 42,
                ["Urgent"] = 21
            },

            // User statistics
            TotalUsers = 1250,
            ActiveUsersThisMonth = 380,
            TotalEmailsSent = 15670,

            // Performance metrics
            ApprovalRate = 83.3,
            ResolutionRate = 31.7,
            BacklogCount = 75,

            // Period information
            Period = "monthly",
            GeneratedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Example provider for AuthStatusResponse
/// </summary>
public class AuthStatusResponseExample : IExamplesProvider<AuthStatusResponse>
{
    public AuthStatusResponse GetExamples()
    {
        return new AuthStatusResponse
        {
            Authenticated = true,
            SupabaseUserId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            Email = "ion.popescu@example.com"
        };
    }
}

/// <summary>
/// Example provider for ActivityResponse
/// </summary>
public class ActivityResponseExample : IExamplesProvider<ActivityResponse>
{
    public ActivityResponse GetExamples()
    {
        return new ActivityResponse
        {
            Id = Guid.NewGuid(),
            Type = ActivityType.IssueCreated,
            IssueId = Guid.NewGuid(),
            IssueTitle = "Groapă periculoasă pe strada Mihai Eminescu",
            Message = "Ion Popescu a raportat o problemă nouă",
            AggregatedCount = 1,
            ActorDisplayName = "Ion Popescu",
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
    }
}

/// <summary>
/// Example provider for CommentResponse
/// </summary>
public class CommentResponseExample : IExamplesProvider<CommentResponse>
{
    public CommentResponse GetExamples()
    {
        return new CommentResponse
        {
            Id = Guid.NewGuid(),
            IssueId = Guid.NewGuid(),
            Content = "Am observat și eu această problemă. Este foarte periculoasă pentru pietoni.",
            HelpfulCount = 5,
            IsEdited = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            ParentCommentId = null,
            ReplyCount = 2,
            User = new CommentUserResponse
            {
                Id = Guid.NewGuid(),
                DisplayName = "Maria Ionescu",
                PhotoUrl = "https://storage.civiti.ro/avatars/user-456.jpg",
                Level = 3
            },
            HasVoted = false
        };
    }
}

/// <summary>
/// Example provider for AuthorityListResponse
/// </summary>
public class AuthorityListResponseExample : IExamplesProvider<AuthorityListResponse>
{
    public AuthorityListResponse GetExamples()
    {
        return new AuthorityListResponse
        {
            Id = Guid.NewGuid(),
            Name = "Primăria Sector 2",
            Email = "contact@sector2.ro",
            City = "București",
            District = "Sector 2"
        };
    }
}