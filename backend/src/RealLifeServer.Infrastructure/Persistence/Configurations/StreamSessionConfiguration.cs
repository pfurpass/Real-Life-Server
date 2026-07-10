using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Configurations;

public class StreamSessionConfiguration : IEntityTypeConfiguration<StreamSession>
{
    public void Configure(EntityTypeBuilder<StreamSession> builder)
    {
        builder.ToTable("stream_sessions");

        builder.Property(s => s.EndReason).HasMaxLength(256);

        builder.HasIndex(s => new { s.ChannelId, s.Status });

        builder.HasMany(s => s.SceneEvents)
            .WithOne(e => e.StreamSession)
            .HasForeignKey(e => e.StreamSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.MetricSamples)
            .WithOne(m => m.StreamSession)
            .HasForeignKey(m => m.StreamSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
