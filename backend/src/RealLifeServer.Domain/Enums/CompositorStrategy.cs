namespace RealLifeServer.Domain.Enums;

/// <summary>
/// Selects how <c>SceneEncoderProcess</c> switches the visible layer.
/// See docs/CONCEPT.md, chapter 4.
/// </summary>
public enum CompositorStrategy
{
    /// <summary>Persistent FFmpeg process, layer visibility toggled live via the zmq filter. No reconnect to the target platform on scene change.</summary>
    Zmq = 0,

    /// <summary>Fallback for FFmpeg builds without libzmq: restarts the encoder process on scene change (brief reconnect to the target platform).</summary>
    RestartableFallback = 1
}
