using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.IssueTitle)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.ActorDisplayName)
            .HasMaxLength(100);

        builder.Property(a => a.Metadata)
            .HasColumnType("jsonb");

        builder.Property(a => a.AggregatedCount)
            .HasDefaultValue(1);

        // Single column indexes
        builder.HasIndex(a => a.IssueOwnerUserId);
        builder.HasIndex(a => a.IssueId);
        builder.HasIndex(a => a.Type);
        builder.HasIndex(a => a.CreatedAt).IsDescending();

        // Composite index for efficient user feed queries
        builder.HasIndex(a => new { a.IssueOwnerUserId, a.CreatedAt })
            .IsDescending(false, true);

        // Relationships
        builder.HasOne(a => a.Issue)
            .WithMany(i => i.Activities)
            .HasForeignKey(a => a.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.IssueOwner)
            .WithMany()
            .HasForeignKey(a => a.IssueOwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
