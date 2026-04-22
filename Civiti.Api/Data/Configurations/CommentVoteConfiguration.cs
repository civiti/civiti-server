using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class CommentVoteConfiguration : IEntityTypeConfiguration<CommentVote>
{
    public void Configure(EntityTypeBuilder<CommentVote> builder)
    {
        builder.HasKey(v => v.Id);

        // Unique constraint: one vote per user per comment
        builder.HasIndex(v => new { v.CommentId, v.UserId })
            .IsUnique();

        // Note: Comment relationship is configured in CommentConfiguration.cs
        // Only configure the User relationship here to avoid duplication
        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
