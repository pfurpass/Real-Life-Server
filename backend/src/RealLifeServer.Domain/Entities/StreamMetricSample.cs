using RealLifeServer.Domain.Common;

namespace RealLifeServer.Domain.Entities;

/// <summary>A single point-in-time telemetry sample for a running stream session. Sampled every ~2s.</summary>
public class StreamMetricSample : BaseEntity
{
    public Guid StreamSessionId { get; set; }
    public StreamSession? StreamSession { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    public int BitrateKbps { get; set; }
    public double PacketLossPercent { get; set; }
    public double FpsIn { get; set; }

    /// <summary>Host-level metrics of the media node running the compositor for this session.</summary>
    public double CpuUsagePercent { get; set; }
    public double RamUsagePercent { get; set; }
    public double NetworkUsageMbps { get; set; }
}
