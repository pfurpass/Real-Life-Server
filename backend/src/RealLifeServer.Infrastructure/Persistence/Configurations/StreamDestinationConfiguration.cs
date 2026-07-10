using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Configurations;

public class StreamDestinationConfiguration : IEntityTypeConfiguration<StreamDestination>
{
    public void Configure(EntityTypeBuilder<StreamDestination> builder)
    {
        builder.ToTable("stream_destinations");

        builder.Property(d => d.RtmpUrl).HasMaxLength(512).IsRequired();
        builder.Property(d => d.StreamKeyEncrypted).HasMaxLength(512).IsRequired();

        builder.HasIndex(d => d.ChannelId);
    }
}
