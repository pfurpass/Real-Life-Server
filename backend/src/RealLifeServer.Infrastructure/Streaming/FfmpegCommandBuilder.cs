using System.Text;
using Microsoft.Extensions.Options;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Streaming;

/// <param name="HasLiveInput">
/// True if input 0 is the RTSP live feed; false if it is a trivial, always-available filler
/// (see docs/CONCEPT.md chapter 4.1 - MediaMTX does not let a reader wait for an absent
/// publisher, so the live input is only ever opened once one is confirmed present).
/// </param>
public sealed record CompositorPlan(
    string Executable,
    IReadOnlyList<string> Arguments,
    int ZmqPort,
    bool HasLiveInput,
    string BrbOverlayName,
    string ReconnectingOverlayName,
    string OfflineOverlayName,
    string CountdownTextName);

/// <summary>
/// Builds the FFmpeg command line for the per-channel compositor process described in
/// docs/CONCEPT.md chapter 4. Two mutually exclusive variants exist, selected by
/// <paramref name="includeLiveInput"/> in <see cref="BuildZmqCompositor"/>:
///
///  - <c>includeLiveInput: true</c> ("LiveInput mode"): input 0 is the RTSP live feed. Only
///    built once a publisher is already confirmed present (after FirstKeyframeReceived).
///  - <c>includeLiveInput: false</c> ("NoLiveInput mode"): input 0 is a trivial `lavfi color`
///    filler that never fails to open, so this variant can run indefinitely without a mobile
///    encoder connected (Offline/Connecting/Reconnecting/Brb).
///
/// Both variants share the same 3 toggleable overlay layers (Brb/Reconnecting/Offline),
/// switched at runtime via the `zmq` filter with no process restart. Switching between the two
/// variants themselves *does* require a process restart (see ZmqSceneEncoder) - FFmpeg's filter
/// graph is static once built and cannot gain or lose an input via zmq commands.
/// </summary>
public class FfmpegCommandBuilder(IOptions<MediaMtxOptions> mediaMtxOptions, IOptions<SceneAssetOptions> assetOptions)
{
    private readonly MediaMtxOptions _mediaMtx = mediaMtxOptions.Value;
    private readonly SceneAssetOptions _assets = assetOptions.Value;

    public CompositorPlan BuildZmqCompositor(Channel channel, IReadOnlyList<string> destinationRtmpUrls, int zmqPort, bool includeLiveInput)
    {
        const string brbOverlay = "ovBrb";
        const string reconnectOverlay = "ovReconnect";
        const string offlineOverlay = "ovOffline";
        const string countdownText = "txtCountdown";

        var w = _assets.CanvasWidth;
        var h = _assets.CanvasHeight;

        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostats" };

        if (includeLiveInput)
        {
            var liveUrl = $"{_mediaMtx.RtspBaseUrl}/{_mediaMtx.PathPrefix}/{channel.StreamKey}";
            args.AddRange(new[] { "-rtsp_transport", "tcp", "-timeout", "5000000", "-i", liveUrl });
        }
        else
        {
            // Infinite-duration filler: `color` has no fixed length unless `:d=` is given.
            args.AddRange(new[] { "-f", "lavfi", "-i", $"color=c=black:s={w}x{h}:r=30" });
        }

        args.AddRange(new[] { "-stream_loop", "-1", "-i", _assets.BrbVideoPath });
        args.AddRange(new[] { "-stream_loop", "-1", "-i", _assets.ReconnectingVideoPath });
        args.AddRange(new[] { "-stream_loop", "-1", "-i", _assets.OfflineVideoPath });

        var audioInputIndex = -1;
        if (!includeLiveInput)
        {
            // The filler base has no audio track, and none of the local slate videos' own audio
            // is mapped (avoids looping mismatched audio) - without this, BRB/Offline/Reconnecting
            // periods (the majority of a channel's runtime) would publish video with no audio
            // track at all, which some players/platforms handle poorly.
            audioInputIndex = 4;
            args.AddRange(new[] { "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo" });
        }

        var filter = BuildFilterGraph(w, h, brbOverlay, reconnectOverlay, offlineOverlay, countdownText, defaultShowOffline: !includeLiveInput, zmqPort);
        args.AddRange(new[] { "-filter_complex", filter, "-map", "[outv]" });
        args.AddRange(includeLiveInput ? new[] { "-map", "0:a?" } : new[] { "-map", $"{audioInputIndex}:a" });

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

        return new CompositorPlan(_assets.FfmpegPath, args, zmqPort, includeLiveInput, brbOverlay, reconnectOverlay, offlineOverlay, countdownText);
    }

    /// <summary>
    /// Input 0 ("base") is either the live feed or the filler, depending on the caller. Base
    /// always shows through by default (no overlay@ node controls it); Brb/Reconnecting/Offline
    /// are chained overlays on top, each independently toggleable and each corresponding to an
    /// actual `overlay@name` filter instance - the very thing the previous version got wrong
    /// (ovBrb was declared but never created; the node that actually controlled the BRB layer
    /// was misnamed ovLive).
    /// </summary>
    private static string BuildFilterGraph(int w, int h, string brbOverlay, string reconnectOverlay, string offlineOverlay, string countdownText, bool defaultShowOffline, int zmqPort)
    {
        var filter = new StringBuilder();
        filter.Append($"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setpts=PTS-STARTPTS[base];");
        filter.Append($"[1:v]scale={w}:{h},setpts=PTS-STARTPTS[brb];");
        filter.Append($"[2:v]scale={w}:{h},setpts=PTS-STARTPTS,");
        filter.Append($"drawtext@{countdownText}=fontcolor=white:fontsize=42:x=(w-text_w)/2:y=(h-text_h)/2+140:");
        filter.Append("text='Verbindung wird wiederhergestellt…'[reconnecting];");
        filter.Append($"[3:v]scale={w}:{h},setpts=PTS-STARTPTS[offline];");
        filter.Append($"[base][brb]overlay@{brbOverlay}=enable=0[ov1];");
        filter.Append($"[ov1][reconnecting]overlay@{reconnectOverlay}=enable=0[ov2];");
        // Colons inside a filtergraph option value must be backslash-escaped (the colon is the
        // option separator); the C# string literal `\\` below yields exactly one literal
        // backslash character each, producing tcp\://127.0.0.1\:<port> in the argument FFmpeg sees.
        filter.Append($"[ov2][offline]overlay@{offlineOverlay}=enable={(defaultShowOffline ? 1 : 0)}[ov3];");
        filter.Append($"[ov3]zmq=bind_address=tcp\\://127.0.0.1\\:{zmqPort}[outv]");
        return filter.ToString();
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
