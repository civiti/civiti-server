using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

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
