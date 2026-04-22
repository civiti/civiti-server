using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Infrastructure.Data.Configurations;

public class BlockedUserConfiguration : IEntityTypeConfiguration<BlockedUser>
{
    public void Configure(EntityTypeBuilder<BlockedUser> builder)
    {
        builder.HasKey(b => b.Id);

        // Unique index: one block relationship per pair
        builder.HasIndex(b => new { b.UserId, b.BlockedUserId })
            .IsUnique();

        // Index for efficient cascade-delete and reverse-lookup (who has blocked me?)
        builder.HasIndex(b => b.BlockedUserId);

        // Relationships
        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Blocked)
            .WithMany()
            .HasForeignKey(b => b.BlockedUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
