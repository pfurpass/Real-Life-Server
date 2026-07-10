namespace RealLifeServer.Domain.Entities;

/// <summary>
/// Per-channel configurable thresholds driving the scene state machine.
/// Persisted as JSONB on <see cref="Channel"/>. See docs/CONCEPT.md, chapter 5.3.
/// </summary>
public class SceneThresholds
{
    public int HeartbeatTimeoutSeconds { get; set; } = 5;
    public int ReconnectTimeoutSeconds { get; set; } = 20;
    public int MinBitrateKbps { get; set; } = 1500;
    public double MaxPacketLossPercent { get; set; } = 3.0;
    public int RecoverySampleCount { get; set; } = 3;
    public int ConnectTimeoutSeconds { get; set; } = 10;
}
