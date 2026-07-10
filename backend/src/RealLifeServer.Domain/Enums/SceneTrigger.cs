namespace RealLifeServer.Domain.Enums;

/// <summary>
/// Events that can trigger a scene state transition. Emitted by the monitoring
/// hosted services and fed into the pure <c>SceneStateMachine</c>.
/// </summary>
public enum SceneTrigger
{
    EncoderConnected,
    FirstKeyframeReceived,
    ConnectTimeoutElapsed,
    BitrateBelowThreshold,
    PacketLossAboveThreshold,
    MetricsRecovered,
    EncoderDisconnected,
    ReconnectTimeoutElapsed,
    ManualStop,
    ChannelSuspended,
    ManualOverride
}
