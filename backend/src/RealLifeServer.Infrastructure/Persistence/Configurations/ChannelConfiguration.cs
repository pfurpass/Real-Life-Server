using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Configurations;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("channels");

        builder.Property(c => c.Name).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(64).IsRequired();
        builder.Property(c => c.StreamKey).HasMaxLength(64).IsRequired();

        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => c.StreamKey).IsUnique();

        // SceneThresholds is a small value object persisted as JSONB - no separate table needed,
        // and it is always loaded together with the channel (docs/CONCEPT.md chapter 7).
        builder.Property(c => c.SceneThresholds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<SceneThresholds>(v, (JsonSerializerOptions?)null) ?? new SceneThresholds())
            .HasColumnType("jsonb")
            .HasColumnName("scene_thresholds");

        builder.HasMany(c => c.Destinations)
            .WithOne(d => d.Channel)
            .HasForeignKey(d => d.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Sessions)
            .WithOne(s => s.Channel)
            .HasForeignKey(s => s.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
