using System.Text.Json.Serialization;

namespace RealLifeServer.Domain.Enums;

/// <summary>
/// Events that can trigger a scene state transition. Emitted by the monitoring
/// hosted services and fed into the pure <c>SceneStateMachine</c>.
/// Serialized as its member name - the frontend's SceneTrigger type is already a string union.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
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
