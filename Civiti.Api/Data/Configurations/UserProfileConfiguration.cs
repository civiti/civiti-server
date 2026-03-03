using Civiti.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(u => u.Id);
        
        builder.Property(u => u.SupabaseUserId)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.HasIndex(u => u.SupabaseUserId)
            .IsUnique();
            
        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.HasIndex(u => u.Email);
            
        builder.Property(u => u.DisplayName)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(u => u.PhotoUrl)
            .HasMaxLength(1000);
            
        builder.Property(u => u.County)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("București");
            
        builder.Property(u => u.City)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("București");
            
        builder.Property(u => u.District)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("Sector 5");
            
        // ResidenceType enum stored as integer by EF Core default
            
        builder.Property(u => u.Points)
            .HasDefaultValue(0);
            
        builder.Property(u => u.Level)
            .HasDefaultValue(1);
            
        builder.Property(u => u.QualityScore)
            .HasPrecision(5, 2)
            .HasDefaultValue(0);
            
        builder.Property(u => u.ApprovalRate)
            .HasPrecision(5, 2)
            .HasDefaultValue(0);

        builder.Property(u => u.IsDeleted)
            .HasDefaultValue(false);

        builder.HasQueryFilter(u => !u.IsDeleted);

        // Indexes
        builder.HasIndex(u => u.IsDeleted)
            .HasFilter("\"IsDeleted\" = true");
        builder.HasIndex(u => u.District);
        builder.HasIndex(u => u.Points).IsDescending();
        builder.HasIndex(u => u.Level).IsDescending();
        
        // Relationships
        builder.HasMany(u => u.Issues)
            .WithOne(i => i.User)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(u => u.UserBadges)
            .WithOne(ub => ub.User)
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(u => u.UserAchievements)
            .WithOne(ua => ua.User)
            .HasForeignKey(ua => ua.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}