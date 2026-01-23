using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

public class CommentVoteConfiguration : IEntityTypeConfiguration<CommentVote>
{
    public void Configure(EntityTypeBuilder<CommentVote> builder)
    {
        builder.HasKey(v => v.Id);

        // Unique constraint: one vote per user per comment
        builder.HasIndex(v => new { v.CommentId, v.UserId })
            .IsUnique();

        // Relationships
        builder.HasOne(v => v.Comment)
            .WithMany(c => c.Votes)
            .HasForeignKey(v => v.CommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
