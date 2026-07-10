using System.Text.Json.Serialization;

namespace RealLifeServer.Domain.Enums;

/// <summary>
/// States of the per-channel scene state machine. See docs/CONCEPT.md, chapter 5.
/// Serialized as its member name - the frontend's SceneState type is already a string union
/// ('Offline' | 'Connecting' | ...), so without this the wire format silently didn't match it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SceneState
{
    Offline = 0,
    Connecting = 1,
    Live = 2,
    Degraded = 3,
    Reconnecting = 4,
    Brb = 5
}
