using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Civica.Api.Models.Domain;

namespace Civica.Api.Data.Configurations;

public class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.HasKey(i => i.Id);
        
        builder.Property(i => i.Title)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(i => i.Description)
            .IsRequired()
            .HasMaxLength(5000);
            
        builder.Property(i => i.Category)
            .HasConversion<string>()
            .HasMaxLength(50);
            
        builder.Property(i => i.Address)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(i => i.Neighborhood)
            .HasMaxLength(100);
            
        builder.Property(i => i.District)
            .HasMaxLength(100);
            
        builder.Property(i => i.Landmark)
            .HasMaxLength(200);

        builder.Property(i => i.EstimatedImpact)
            .HasColumnType("integer");
            
        builder.Property(i => i.Tags)
            .HasMaxLength(500);
            
        builder.Property(i => i.Urgency)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(UrgencyLevel.Medium)
            .HasSentinel(UrgencyLevel.Unspecified);
            
        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(IssueStatus.Submitted)
            .HasSentinel(IssueStatus.Unspecified);
            
        builder.Property(i => i.Priority)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Priority.Medium)
            .HasSentinel(Priority.Unspecified);
            
        builder.Property(i => i.AssignedDepartment)
            .HasMaxLength(100);
            
        builder.Property(i => i.EstimatedResolutionTime)
            .HasMaxLength(50);
            
        builder.Property(i => i.ReviewedBy)
            .HasMaxLength(255);
            
        builder.Property(i => i.AIConfidence)
            .HasPrecision(3, 2);
            
        // Indexes
        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.Category);
        builder.HasIndex(i => i.Urgency);
        builder.HasIndex(i => i.District);
        builder.HasIndex(i => i.CreatedAt).IsDescending();
        builder.HasIndex(i => i.EmailsSent).IsDescending();
        builder.HasIndex(i => new { i.Status, i.PublicVisibility })
            .HasFilter("\"Status\" = 'Active' AND \"PublicVisibility\" = true");

        // Composite index for main query
        builder.HasIndex(i => new { i.Status, i.PublicVisibility, i.CreatedAt })
            .IsDescending()
            .HasFilter("\"Status\" = 'Active' AND \"PublicVisibility\" = true");
            
        // Relationships
        builder.HasOne(i => i.User)
            .WithMany(u => u.Issues)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(i => i.Photos)
            .WithOne(p => p.Issue)
            .HasForeignKey(p => p.IssueId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(i => i.AdminActions)
            .WithOne(a => a.Issue)
            .HasForeignKey(a => a.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.IssueAuthorities)
            .WithOne(ia => ia.Issue)
            .HasForeignKey(ia => ia.IssueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}