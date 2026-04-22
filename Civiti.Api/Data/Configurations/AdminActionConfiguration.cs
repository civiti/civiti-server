using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class AdminActionConfiguration : IEntityTypeConfiguration<AdminAction>
{
    public void Configure(EntityTypeBuilder<AdminAction> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AdminSupabaseId)
            .HasMaxLength(255);

        // ActionType (AdminActionType enum) stored as integer by EF Core default

        builder.Property(a => a.Notes)
            .HasMaxLength(1000);

        builder.Property(a => a.PreviousStatus)
            .HasMaxLength(30);

        builder.Property(a => a.NewStatus)
            .HasMaxLength(30);

        // Indexes
        builder.HasIndex(a => a.IssueId);
        builder.HasIndex(a => a.AdminUserId);
        builder.HasIndex(a => a.ActionType);
        builder.HasIndex(a => a.CreatedAt).IsDescending();

        // Relationships
        builder.HasOne(a => a.Issue)
            .WithMany(i => i.AdminActions)
            .HasForeignKey(a => a.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.AdminUser)
            .WithMany()
            .HasForeignKey(a => a.AdminUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}