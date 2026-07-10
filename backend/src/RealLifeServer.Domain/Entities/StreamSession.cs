using RealLifeServer.Domain.Common;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Domain.Entities;

/// <summary>One continuous "on-air" lifecycle of a channel, from first connect to explicit stop.</summary>
public class StreamSession : BaseEntity
{
    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public StreamSessionStatus Status { get; set; } = StreamSessionStatus.Running;
    public string? EndReason { get; set; }

    public ICollection<SceneEvent> SceneEvents { get; set; } = new List<SceneEvent>();
    public ICollection<StreamMetricSample> MetricSamples { get; set; } = new List<StreamMetricSample>();
}
