using RealLifeServer.Domain.Common;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Domain.Entities;

public class Channel : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    public User? Owner { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Ingest stream key, e.g. rtmp://server/live/{StreamKey}. Rotatable.</summary>
    public string StreamKey { get; set; } = string.Empty;

    public IngestProtocol IngestProtocols { get; set; } = IngestProtocol.Rtmp | IngestProtocol.Srt;

    public bool IsActive { get; set; } = true;

    /// <summary>Cached copy of the state machine's current state for fast dashboard reads.</summary>
    public SceneState CurrentSceneState { get; set; } = SceneState.Offline;

    public SceneThresholds SceneThresholds { get; set; } = new();

    public CompositorStrategy CompositorStrategy { get; set; } = CompositorStrategy.Zmq;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<StreamDestination> Destinations { get; set; } = new List<StreamDestination>();
    public ICollection<StreamSession> Sessions { get; set; } = new List<StreamSession>();
}
