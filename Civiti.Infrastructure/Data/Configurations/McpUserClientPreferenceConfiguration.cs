using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Infrastructure.Data.Configurations;

public class McpUserClientPreferenceConfiguration : IEntityTypeConfiguration<McpUserClientPreference>
{
    public void Configure(EntityTypeBuilder<McpUserClientPreference> builder)
    {
        builder.ToTable("McpUserClientPreferences");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.SupabaseUserId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.ClientId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.ScopesGranted)
            .HasColumnType("text[]");

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        // One consent record per (user, client). Re-consent updates UpdatedAt + ScopesGranted
        // in place; we never want two rows fighting over which scopes a user remembers.
        builder.HasIndex(p => new { p.SupabaseUserId, p.ClientId })
            .IsUnique();
    }
}
