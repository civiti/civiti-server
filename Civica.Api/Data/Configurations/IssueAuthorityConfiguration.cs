using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

public class IssueAuthorityConfiguration : IEntityTypeConfiguration<IssueAuthority>
{
    public void Configure(EntityTypeBuilder<IssueAuthority> builder)
    {
        builder.HasKey(ia => ia.Id);

        builder.Property(ia => ia.CustomName)
            .HasMaxLength(200);

        builder.Property(ia => ia.CustomEmail)
            .HasMaxLength(255);

        // Unique index for predefined authorities (one authority per issue)
        builder.HasIndex(ia => new { ia.IssueId, ia.AuthorityId })
            .IsUnique()
            .HasFilter("\"AuthorityId\" IS NOT NULL");

        // Unique index for custom authorities (one custom email per issue)
        builder.HasIndex(ia => new { ia.IssueId, ia.CustomEmail })
            .IsUnique()
            .HasFilter("\"CustomEmail\" IS NOT NULL");

        // Index for efficient queries
        builder.HasIndex(ia => ia.IssueId);
        builder.HasIndex(ia => ia.AuthorityId);

        // Relationships
        builder.HasOne(ia => ia.Issue)
            .WithMany(i => i.IssueAuthorities)
            .HasForeignKey(ia => ia.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ia => ia.Authority)
            .WithMany(a => a.IssueAuthorities)
            .HasForeignKey(ia => ia.AuthorityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Check constraint: either AuthorityId OR (CustomName AND CustomEmail) must be set
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_IssueAuthority_AuthorityOrCustom",
            "(\"AuthorityId\" IS NOT NULL AND \"CustomName\" IS NULL AND \"CustomEmail\" IS NULL) OR " +
            "(\"AuthorityId\" IS NULL AND \"CustomName\" IS NOT NULL AND \"CustomEmail\" IS NOT NULL)"));
    }
}
