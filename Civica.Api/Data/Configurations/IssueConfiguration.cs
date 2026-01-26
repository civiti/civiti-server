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
            
        builder.Property(i => i.Address)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(i => i.District)
            .HasMaxLength(100);

        // Enums (Category, Urgency, Status) stored as integers by EF Core default
            
        builder.Property(i => i.ReviewedBy)
            .HasMaxLength(255);

        // Indexes
        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.Category);
        builder.HasIndex(i => i.Urgency);
        builder.HasIndex(i => i.District);
        builder.HasIndex(i => i.CreatedAt).IsDescending();
        builder.HasIndex(i => i.EmailsSent).IsDescending();
        builder.HasIndex(i => i.CommunityVotes).IsDescending();
        // Partial index for active public issues (Status = Active = 4)
        builder.HasIndex(i => new { i.Status, i.PublicVisibility })
            .HasFilter("\"Status\" = 4 AND \"PublicVisibility\" = true");

        // Composite index for main query
        builder.HasIndex(i => new { i.Status, i.PublicVisibility, i.CreatedAt })
            .IsDescending()
            .HasFilter("\"Status\" = 4 AND \"PublicVisibility\" = true");
            
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

        builder.HasMany(i => i.Votes)
            .WithOne(v => v.Issue)
            .HasForeignKey(v => v.IssueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}