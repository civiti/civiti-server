using Civiti.Infrastructure.Data.Configurations;
using Civiti.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Infrastructure.Data;

public class CivitiDbContext(DbContextOptions<CivitiDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    // Civiti.Auth's Data Protection key ring lives on this context so encrypted PKCE state
    // survives container restarts (Railway redeploys would otherwise discard the in-memory
    // keys and fail any in-flight Supabase login round-trip with "State parameter is missing
    // or tampered"). Other hosts don't currently use Data Protection but inherit the table
    // for free since they share the DbContext.
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    public DbSet<UserProfile> UserProfiles { get; set; } = null!;
    public DbSet<Issue> Issues { get; set; } = null!;
    public DbSet<IssuePhoto> IssuePhotos { get; set; } = null!;
    public DbSet<Badge> Badges { get; set; } = null!;
    public DbSet<Achievement> Achievements { get; set; } = null!;
    public DbSet<UserBadge> UserBadges { get; set; } = null!;
    public DbSet<UserAchievement> UserAchievements { get; set; } = null!;
    public DbSet<AdminAction> AdminActions { get; set; } = null!;
    public DbSet<Authority> Authorities { get; set; } = null!;
    public DbSet<IssueAuthority> IssueAuthorities { get; set; } = null!;
    public DbSet<Activity> Activities { get; set; } = null!;
    public DbSet<Comment> Comments { get; set; } = null!;
    public DbSet<CommentVote> CommentVotes { get; set; } = null!;
    public DbSet<IssueVote> IssueVotes { get; set; } = null!;
    public DbSet<PushToken> PushTokens { get; set; } = null!;
    public DbSet<Report> Reports { get; set; } = null!;
    public DbSet<BlockedUser> BlockedUsers { get; set; } = null!;
    public DbSet<AdminIssueNotification> AdminIssueNotifications { get; set; } = null!;
    public DbSet<McpSession> McpSessions { get; set; } = null!;
    public DbSet<McpUserClientPreference> McpUserClientPreferences { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Note: Enums are stored as integers (EF Core default)
        // This avoids PostgreSQL native enum complexity with migrations

        // Apply configurations
        modelBuilder.ApplyConfiguration(new UserProfileConfiguration());
        modelBuilder.ApplyConfiguration(new IssueConfiguration());
        modelBuilder.ApplyConfiguration(new IssuePhotoConfiguration());
        modelBuilder.ApplyConfiguration(new BadgeConfiguration());
        modelBuilder.ApplyConfiguration(new AchievementConfiguration());
        modelBuilder.ApplyConfiguration(new UserBadgeConfiguration());
        modelBuilder.ApplyConfiguration(new UserAchievementConfiguration());
        modelBuilder.ApplyConfiguration(new AdminActionConfiguration());
        modelBuilder.ApplyConfiguration(new AuthorityConfiguration());
        modelBuilder.ApplyConfiguration(new IssueAuthorityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityConfiguration());
        modelBuilder.ApplyConfiguration(new CommentConfiguration());
        modelBuilder.ApplyConfiguration(new CommentVoteConfiguration());
        modelBuilder.ApplyConfiguration(new IssueVoteConfiguration());
        modelBuilder.ApplyConfiguration(new PushTokenConfiguration());
        modelBuilder.ApplyConfiguration(new ReportConfiguration());
        modelBuilder.ApplyConfiguration(new BlockedUserConfiguration());
        modelBuilder.ApplyConfiguration(new AdminIssueNotificationConfiguration());
        modelBuilder.ApplyConfiguration(new McpSessionConfiguration());
        modelBuilder.ApplyConfiguration(new McpUserClientPreferenceConfiguration());

        // Register OpenIddict's default EF entity set (applications, authorizations, scopes,
        // tokens). Matches architecture.md §3: all OAuth state lives on the shared DbContext,
        // with Civiti.Api as the sole migration runner; Civiti.Auth and Civiti.Mcp read/write
        // via DI but never call Database.Migrate().
        modelBuilder.UseOpenIddict();

        // Note: Seed data is handled by StaticDataSeeder at runtime
    }
}
