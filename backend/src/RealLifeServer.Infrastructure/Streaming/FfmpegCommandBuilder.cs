using System.Text;
using Microsoft.Extensions.Options;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Streaming;

public sealed record CompositorPlan(
    string Executable,
    IReadOnlyList<string> Arguments,
    int ZmqPort,
    string LiveOverlayName,
    string BrbOverlayName,
    string ReconnectingOverlayName,
    string OfflineOverlayName,
    string CountdownTextName);

/// <summary>
/// Builds the FFmpeg command line for the persistent per-channel compositor process described
/// in docs/CONCEPT.md chapter 4: one live RTSP input (read from MediaMTX) plus three looped
/// local slate videos, composited via chained `overlay` filters whose visibility is toggled at
/// runtime through the `zmq` filter - never by restarting the process.
/// </summary>
public class FfmpegCommandBuilder(IOptions<MediaMtxOptions> mediaMtxOptions, IOptions<SceneAssetOptions> assetOptions)
{
    private readonly MediaMtxOptions _mediaMtx = mediaMtxOptions.Value;
    private readonly SceneAssetOptions _assets = assetOptions.Value;

    public CompositorPlan BuildZmqCompositor(Channel channel, IReadOnlyList<string> destinationRtmpUrls, int zmqPort)
    {
        const string liveOverlay = "ovLive";
        const string brbOverlay = "ovBrb";
        const string reconnectOverlay = "ovReconnect";
        const string offlineOverlay = "ovOffline";
        const string countdownText = "txtCountdown";

        var w = _assets.CanvasWidth;
        var h = _assets.CanvasHeight;
        var liveUrl = $"{_mediaMtx.RtspBaseUrl}/{_mediaMtx.PathPrefix}/{channel.StreamKey}";

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-nostats",

            // Live source via RTSP: MediaMTX lets a reader attach and wait even without an
            // active publisher, so this input never needs the process itself to be restarted
            // when the encoder drops (see docs/CONCEPT.md chapter 4.1).
            "-rtsp_transport", "tcp", "-timeout", "5000000", "-i", liveUrl,

            "-stream_loop", "-1", "-i", _assets.BrbVideoPath,
            "-stream_loop", "-1", "-i", _assets.ReconnectingVideoPath,
            "-stream_loop", "-1", "-i", _assets.OfflineVideoPath,
        };

        // Note on the "@name" syntax: FFmpeg requires the instance name to come immediately
        // after the filter name and before its options (`filter@name=opt1:opt2`), not after
        // them - that's what makes these instances addressable by the zmq filter later.
        var filter = new StringBuilder();
        filter.Append($"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setpts=PTS-STARTPTS[live];");
        filter.Append($"[1:v]scale={w}:{h},setpts=PTS-STARTPTS[brb];");
        filter.Append($"[2:v]scale={w}:{h},setpts=PTS-STARTPTS,");
        filter.Append($"drawtext@{countdownText}=fontcolor=white:fontsize=42:x=(w-text_w)/2:y=(h-text_h)/2+140:");
        filter.Append("text='Verbindung wird wiederhergestellt…'[reconnecting];");
        filter.Append($"[3:v]scale={w}:{h},setpts=PTS-STARTPTS[offline];");
        filter.Append($"[live][brb]overlay@{liveOverlay}=enable=0[ov1];");
        filter.Append($"[ov1][reconnecting]overlay@{reconnectOverlay}=enable=0[ov2];");
        filter.Append($"[ov2][offline]overlay@{offlineOverlay}=enable=1[ov3];");
        // Colons inside a filtergraph option value must be backslash-escaped (the colon is the
        // option separator); the C# string literal `\\` below yields exactly one literal
        // backslash character each, producing tcp\://127.0.0.1\:<port> in the argument FFmpeg sees.
        filter.Append($"[ov3]zmq=bind_address=tcp\\://127.0.0.1\\:{zmqPort}[outv]");

        args.AddRange(new[] { "-filter_complex", filter.ToString() });
        args.AddRange(new[] { "-map", "[outv]", "-map", "0:a?" });

        var encoder = string.IsNullOrWhiteSpace(_assets.HardwareEncoder) ? "libx264" : _assets.HardwareEncoder!;
        args.AddRange(new[]
        {
            "-c:v", encoder,
            "-preset", encoder == "libx264" ? _assets.X264Preset : "p4",
            "-b:v", $"{_assets.VideoBitrateKbps}k",
            "-maxrate", $"{_assets.VideoBitrateKbps}k",
            "-bufsize", $"{_assets.VideoBitrateKbps * 2}k",
            "-g", "60",
            "-c:a", "aac", "-b:a", "160k", "-ar", "44100"
        });

        args.AddRange(BuildOutputArgs(destinationRtmpUrls));

        return new CompositorPlan(_assets.FfmpegPath, args, zmqPort, liveOverlay, brbOverlay, reconnectOverlay, offlineOverlay, countdownText);
    }

    private static IEnumerable<string> BuildOutputArgs(IReadOnlyList<string> destinationRtmpUrls)
    {
        if (destinationRtmpUrls.Count == 0)
        {
            // No destination configured yet: discard output so the encoder still runs and can
            // be observed/tested, but nothing is published externally.
            return new[] { "-f", "null", "-" };
        }

        if (destinationRtmpUrls.Count == 1)
        {
            return new[] { "-f", "flv", destinationRtmpUrls[0] };
        }

        // Multiple simultaneous destinations (e.g. Twitch + YouTube) via the tee muxer.
        var teeTarget = string.Join("|", destinationRtmpUrls.Select(url => $"[f=flv]{url}"));
        return new[] { "-f", "tee", teeTarget };
    }
}
