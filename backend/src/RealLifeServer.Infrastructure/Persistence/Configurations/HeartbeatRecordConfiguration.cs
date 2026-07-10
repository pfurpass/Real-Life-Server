using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Configurations;

public class HeartbeatRecordConfiguration : IEntityTypeConfiguration<HeartbeatRecord>
{
    public void Configure(EntityTypeBuilder<HeartbeatRecord> builder)
    {
        builder.ToTable("heartbeat_records");
        builder.Property(h => h.Source).HasMaxLength(32);
        builder.HasIndex(h => new { h.ChannelId, h.ReceivedAt });
    }
}
