using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Infrastructure.Data.Configurations;

public class IssueApprovedSnapshotConfiguration : IEntityTypeConfiguration<IssueApprovedSnapshot>
{
    public void Configure(EntityTypeBuilder<IssueApprovedSnapshot> builder)
    {
        // One snapshot per issue. Becoming (IssueId, Version) is the upgrade path to full
        // revision history; nothing else in this configuration would have to change.
        builder.HasKey(s => s.IssueId);

        // Stored as plain text rather than jsonb: it is only ever read back whole and
        // deserialised, never queried into, so a JSON column type would buy nothing while
        // making the entity provider-specific (the test harness runs on SQLite).
        builder.Property(s => s.Payload)
            .IsRequired();

        builder.HasOne(s => s.Issue)
            .WithOne()
            .HasForeignKey<IssueApprovedSnapshot>(s => s.IssueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
