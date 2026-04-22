using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class AdminIssueNotificationConfiguration : IEntityTypeConfiguration<AdminIssueNotification>
{
    public void Configure(EntityTypeBuilder<AdminIssueNotification> builder)
    {
        // Composite primary key on (IssueId, AdminEmail) gives per-recipient
        // idempotency via ON CONFLICT DO NOTHING on insert.
        builder.HasKey(n => new { n.IssueId, n.AdminEmail });

        builder.Property(n => n.AdminEmail)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(n => n.EnqueuedAt)
            .IsRequired();

        // Index to quickly find "have we already notified anyone for this issue?"
        // Not strictly needed (composite PK covers it via prefix) but explicit here.
        builder.HasIndex(n => n.IssueId);

        // Cascade delete: if an issue is hard-deleted, its notification audit rows go too.
        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(n => n.IssueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
