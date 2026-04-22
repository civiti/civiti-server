using Civiti.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Civiti.Infrastructure.Data.Configurations;

public class PushTokenConfiguration : IEntityTypeConfiguration<PushToken>
{
    public void Configure(EntityTypeBuilder<PushToken> builder)
    {
        builder.HasKey(pt => pt.Id);

        builder.Property(pt => pt.Token)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(pt => pt.Token)
            .IsUnique();

        builder.HasIndex(pt => pt.UserId);

        builder.HasOne(pt => pt.User)
            .WithMany()
            .HasForeignKey(pt => pt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
