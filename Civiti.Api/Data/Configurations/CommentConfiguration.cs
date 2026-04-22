using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Api.Data.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Content)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(c => c.HelpfulCount)
            .HasDefaultValue(0);

        builder.Property(c => c.IsEdited)
            .HasDefaultValue(false);

        builder.Property(c => c.IsDeleted)
            .HasDefaultValue(false);

        builder.Property(c => c.ReportCount)
            .HasDefaultValue(0);

        builder.Property(c => c.IsHidden)
            .HasDefaultValue(false);

        // Indexes
        builder.HasIndex(c => c.IssueId);
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.ParentCommentId);
        builder.HasIndex(c => c.CreatedAt).IsDescending();
        builder.HasIndex(c => new { c.IssueId, c.IsDeleted, c.CreatedAt });

        // Relationships
        builder.HasOne(c => c.Issue)
            .WithMany(i => i.Comments)
            .HasForeignKey(c => c.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.DeletedByUser)
            .WithMany()
            .HasForeignKey(c => c.DeletedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Votes)
            .WithOne(v => v.Comment)
            .HasForeignKey(v => v.CommentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
