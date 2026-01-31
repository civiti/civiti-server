using Civiti.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class IssueVoteConfiguration : IEntityTypeConfiguration<IssueVote>
{
    public void Configure(EntityTypeBuilder<IssueVote> builder)
    {
        builder.HasKey(v => v.Id);

        // Unique constraint: one vote per user per issue
        builder.HasIndex(v => new { v.IssueId, v.UserId })
            .IsUnique();

        // Index on UserId for efficient lookups of user's votes
        builder.HasIndex(v => v.UserId);

        // Note: Issue relationship is configured in IssueConfiguration.cs
        // Only configure the User relationship here to avoid duplication
        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
