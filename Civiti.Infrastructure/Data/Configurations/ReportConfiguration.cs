using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Infrastructure.Data.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TargetType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.Details)
            .HasMaxLength(500);

        // CHECK constraint: only valid TargetType values
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Reports_TargetType",
            $"\"TargetType\" IN ('{ReportTargetTypes.Issue}', '{ReportTargetTypes.Comment}')"));

        // Unique index: one report per user per target
        builder.HasIndex(r => new { r.ReporterId, r.TargetType, r.TargetId })
            .IsUnique();

        // Index for looking up reports by target
        builder.HasIndex(r => new { r.TargetType, r.TargetId });

        // Index for rate-limit query: per-reporter recent reports
        builder.HasIndex(r => new { r.ReporterId, r.CreatedAt });

        // Relationships — Restrict delete to prevent orphaning ReportCount/IsFlagged
        // on target entities. Reports must be cleaned up before user deletion.
        builder.HasOne(r => r.Reporter)
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
