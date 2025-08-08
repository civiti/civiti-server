using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

public class UserBadgeConfiguration : IEntityTypeConfiguration<UserBadge>
{
    public void Configure(EntityTypeBuilder<UserBadge> builder)
    {
        builder.HasKey(ub => ub.Id);
        
        // Unique constraint to prevent duplicate badges
        builder.HasIndex(ub => new { ub.UserId, ub.BadgeId })
            .IsUnique();
            
        // Indexes
        builder.HasIndex(ub => ub.UserId);
        builder.HasIndex(ub => ub.BadgeId);
        builder.HasIndex(ub => ub.EarnedAt).IsDescending();
        
        // Relationships
        builder.HasOne(ub => ub.User)
            .WithMany(u => u.UserBadges)
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(ub => ub.Badge)
            .WithMany(b => b.UserBadges)
            .HasForeignKey(ub => ub.BadgeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}