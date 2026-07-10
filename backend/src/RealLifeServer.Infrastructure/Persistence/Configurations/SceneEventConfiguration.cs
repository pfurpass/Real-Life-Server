using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Configurations;

public class SceneEventConfiguration : IEntityTypeConfiguration<SceneEvent>
{
    public void Configure(EntityTypeBuilder<SceneEvent> builder)
    {
        builder.ToTable("scene_events");

        builder.Property(e => e.MetadataJson).HasColumnType("jsonb");

        // Primary access pattern: "latest events for a channel" (log panel, chapter 9).
        builder.HasIndex(e => new { e.ChannelId, e.OccurredAt });
    }
}
