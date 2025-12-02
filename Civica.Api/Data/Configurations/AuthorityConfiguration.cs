using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

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

        // Indexes
        builder.HasIndex(a => a.Email)
            .IsUnique();

        builder.HasIndex(a => a.IsActive);

        // Relationships
        builder.HasMany(a => a.IssueAuthorities)
            .WithOne(ia => ia.Authority)
            .HasForeignKey(ia => ia.AuthorityId)
            .OnDelete(DeleteBehavior.Restrict); // Don't delete authority if linked to issues
    }
}
