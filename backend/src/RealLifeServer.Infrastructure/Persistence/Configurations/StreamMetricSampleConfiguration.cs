using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Configurations;

public class StreamMetricSampleConfiguration : IEntityTypeConfiguration<StreamMetricSample>
{
    public void Configure(EntityTypeBuilder<StreamMetricSample> builder)
    {
        builder.ToTable("stream_metric_samples");

        // Highest write-volume table (one row every ~2s per active channel). See
        // docs/CONCEPT.md chapter 12: consider monthly range partitioning + a retention job
        // that rolls rows older than 30 days into stream_metric_hourly_aggregates.
        builder.HasIndex(m => new { m.StreamSessionId, m.RecordedAt });
    }
}
