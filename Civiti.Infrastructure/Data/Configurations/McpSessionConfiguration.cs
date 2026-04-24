using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Infrastructure.Data.Configurations;

public class McpSessionConfiguration : IEntityTypeConfiguration<McpSession>
{
    public void Configure(EntityTypeBuilder<McpSession> builder)
    {
        builder.HasKey(s => s.Id);

        // OpenIddict token ids are GUIDs serialised as strings by the default store; 128 chars
        // is generous. Nullable because a session row briefly exists between token-consumed and
        // new-token-issued during rotation (the transactional boundary in auth-design.md §4).
        builder.Property(s => s.OpenIddictTokenId)
            .HasMaxLength(128);

        builder.Property(s => s.ClientId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.SupabaseUserId)
            .IsRequired()
            .HasMaxLength(64);

        // Native Postgres text[] via Npgsql; no JSON overhead and queryable via ANY/CONTAINS.
        builder.Property(s => s.ScopesGranted)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.LastSeenAt).IsRequired();

        builder.Property(s => s.RevokedReason)
            .HasMaxLength(64);

        // Rotation looks sessions up by their current refresh-token id; must be unique so we
        // can never have two active sessions pointing at the same token row.
        builder.HasIndex(s => s.OpenIddictTokenId)
            .IsUnique();

        // "Active sessions for this user" — powers the Connected AI Assistants UI and the
        // admin kill-switch sweep.
        builder.HasIndex(s => new { s.SupabaseUserId, s.RevokedAt });
    }
}
