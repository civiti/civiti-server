using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

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
            
        builder.Property(b => b.Category)
            .HasConversion<string>()
            .HasMaxLength(30);
            
        builder.Property(b => b.Rarity)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(BadgeRarity.Common);
            
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