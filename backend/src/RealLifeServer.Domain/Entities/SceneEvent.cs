using RealLifeServer.Domain.Common;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Domain.Entities;

/// <summary>Audit trail entry for every scene state transition. Drives the log panel and Discord notifications.</summary>
public class SceneEvent : BaseEntity
{
    public Guid StreamSessionId { get; set; }
    public StreamSession? StreamSession { get; set; }

    public Guid ChannelId { get; set; }

    public SceneState FromState { get; set; }
    public SceneState ToState { get; set; }
    public SceneTrigger Trigger { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Free-form context, e.g. {"bitrateKbps": 420, "packetLossPercent": 7.2}.</summary>
    public string? MetadataJson { get; set; }
}
