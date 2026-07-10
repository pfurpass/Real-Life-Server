namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>Bound from configuration section "SceneAssets". Local looped video files used as fallback scenes (deploy/assets/scenes).</summary>
public class SceneAssetOptions
{
    public string BrbVideoPath { get; set; } = "/assets/scenes/brb.mp4";
    public string ReconnectingVideoPath { get; set; } = "/assets/scenes/reconnecting.mp4";
    public string OfflineVideoPath { get; set; } = "/assets/scenes/offline.mp4";

    public int CanvasWidth { get; set; } = 1280;
    public int CanvasHeight { get; set; } = 720;
    public int VideoBitrateKbps { get; set; } = 4500;
    public string X264Preset { get; set; } = "veryfast";

    /// <summary>First TCP port used for per-channel zmq filter control sockets; each running compositor claims one port from this range.</summary>
    public int ZmqBasePort { get; set; } = 15000;

    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>"h264_nvenc" | "h264_qsv" | null for software libx264. See docs/CONCEPT.md chapter 12.</summary>
    public string? HardwareEncoder { get; set; }
}
