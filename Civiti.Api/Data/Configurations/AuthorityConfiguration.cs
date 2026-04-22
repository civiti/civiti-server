using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class AuthorityConfiguration : IEntityTypeConfiguration<Authority>
{
    public void Configure(EntityTypeBuilder<Authority> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(a => a.County)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.District)
            .HasMaxLength(100);

        // Indexes
        builder.HasIndex(a => a.Email)
            .IsUnique();

        builder.HasIndex(a => a.IsActive);

        builder.HasIndex(a => a.Name);

        builder.HasIndex(a => a.City);

        builder.HasIndex(a => new { a.City, a.District });

        // Relationships
        builder.HasMany(a => a.IssueAuthorities)
            .WithOne(ia => ia.Authority)
            .HasForeignKey(ia => ia.AuthorityId)
            .OnDelete(DeleteBehavior.Restrict); // Don't delete authority if linked to issues
    }
}
