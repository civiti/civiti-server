using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

public class EmailTrackingConfiguration : IEntityTypeConfiguration<EmailTracking>
{
    public void Configure(EntityTypeBuilder<EmailTracking> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.AuthorityEmail)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(e => e.AuthorityName)
            .HasMaxLength(200);
            
        // Unique constraint to prevent duplicate tracking
        builder.HasIndex(e => new { e.IssueId, e.UserId, e.AuthorityEmail })
            .IsUnique();
            
        // Indexes
        builder.HasIndex(e => e.IssueId);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.SentAt).IsDescending();
        
        // Relationships
        builder.HasOne(e => e.Issue)
            .WithMany(i => i.EmailTrackings)
            .HasForeignKey(e => e.IssueId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}