namespace RealLifeServer.Domain.Enums;

/// <summary>
/// States of the per-channel scene state machine. See docs/CONCEPT.md, chapter 5.
/// </summary>
public enum SceneState
{
    Offline = 0,
    Connecting = 1,
    Live = 2,
    Degraded = 3,
    Reconnecting = 4,
    Brb = 5
}
