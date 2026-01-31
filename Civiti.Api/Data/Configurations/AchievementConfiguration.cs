using Civiti.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        builder.HasKey(a => a.Id);
        
        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(a => a.Description)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(a => a.MaxProgress)
            .HasDefaultValue(1);
            
        builder.Property(a => a.RewardPoints)
            .HasDefaultValue(0);
            
        builder.Property(a => a.AchievementType)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(a => a.RequirementData)
            .HasColumnType("jsonb");
            
        // Indexes
        builder.HasIndex(a => a.AchievementType);
        
        // Relationships
        builder.HasOne(a => a.RewardBadge)
            .WithMany()
            .HasForeignKey(a => a.RewardBadgeId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasMany(a => a.UserAchievements)
            .WithOne(ua => ua.Achievement)
            .HasForeignKey(ua => ua.AchievementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}