using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Infrastructure.Data.Configurations;

public class IssuePhotoConfiguration : IEntityTypeConfiguration<IssuePhoto>
{
    public void Configure(EntityTypeBuilder<IssuePhoto> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Url)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(p => p.ThumbnailUrl)
            .HasMaxLength(1000);

        builder.Property(p => p.Caption)
            .HasMaxLength(500);

        // Quality (PhotoQuality enum) stored as integer by EF Core default

        builder.Property(p => p.Format)
            .HasMaxLength(10);

        // Rows written before this column existed default to 0 and fall back to the ordering
        // they were already displayed in — see IssuePhotoOrdering. No backfill needed: a photo
        // set is replaced wholesale, so an issue never mixes old and new rows.
        builder.Property(p => p.DisplayOrder)
            .HasDefaultValue(0);

        // Indexes
        builder.HasIndex(p => p.IssueId);
        builder.HasIndex(p => p.CreatedAt);

        // Relationships
        builder.HasOne(p => p.Issue)
            .WithMany(i => i.Photos)
            .HasForeignKey(p => p.IssueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
