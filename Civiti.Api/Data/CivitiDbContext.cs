using Civiti.Api.Data.Configurations;
using Civiti.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Api.Data;

public class CivitiDbContext(DbContextOptions<CivitiDbContext> options) : DbContext(options)
{
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

        // Note: Seed data is handled by StaticDataSeeder at runtime
    }
}
