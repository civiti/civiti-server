using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class BadgeConfiguration : IEntityTypeConfiguration<Badge>
{
    public void Configure(EntityTypeBuilder<Badge> builder)
    {
        builder.HasKey(b => b.Id);
        
        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.HasIndex(b => b.Name)
            .IsUnique();
            
        builder.Property(b => b.Description)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(b => b.IconUrl)
            .HasMaxLength(500);
            
        // Enums (Category, Rarity) stored as integers by EF Core default
            
        builder.Property(b => b.RequirementType)
            .HasMaxLength(50);
            
        builder.Property(b => b.RequirementDescription)
            .HasMaxLength(500);
            
        // Indexes
        builder.HasIndex(b => b.Category);
        builder.HasIndex(b => b.Rarity);
        
        // Relationships
        builder.HasMany(b => b.UserBadges)
            .WithOne(ub => ub.Badge)
            .HasForeignKey(ub => ub.BadgeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}