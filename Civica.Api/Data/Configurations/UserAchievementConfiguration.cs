using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

public class UserAchievementConfiguration : IEntityTypeConfiguration<UserAchievement>
{
    public void Configure(EntityTypeBuilder<UserAchievement> builder)
    {
        builder.HasKey(ua => ua.Id);
        
        // Unique constraint to prevent duplicate achievements
        builder.HasIndex(ua => new { ua.UserId, ua.AchievementId })
            .IsUnique();
            
        builder.Property(ua => ua.Progress)
            .HasDefaultValue(0);
            
        builder.Property(ua => ua.Completed)
            .HasDefaultValue(false);
            
        // Indexes
        builder.HasIndex(ua => ua.UserId);
        builder.HasIndex(ua => new { ua.Completed, ua.CompletedAt });
        
        // Relationships
        builder.HasOne(ua => ua.User)
            .WithMany(u => u.UserAchievements)
            .HasForeignKey(ua => ua.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(ua => ua.Achievement)
            .WithMany(a => a.UserAchievements)
            .HasForeignKey(ua => ua.AchievementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}