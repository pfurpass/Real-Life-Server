using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Fallback compositor strategy for FFmpeg builds without libzmq support (docs/CONCEPT.md,
/// chapter 4.2). Simpler and more portable than <see cref="ZmqSceneEncoder"/>, but restarts the
/// FFmpeg process - and therefore briefly reconnects to the target platform(s) - on every scene
/// change. Prefer the Zmq strategy in production; this exists so RLS still runs on hosts whose
/// FFmpeg lacks the zmq filter.
/// </summary>
public sealed class RestartableFallbackSceneEncoder(
    Guid channelId,
    IOptions<MediaMtxOptions> mediaMtxOptions,
    IOptions<SceneAssetOptions> assetOptions,
    ILoggerFactory loggerFactory) : ISceneEncoder
{
    private readonly ILogger _logger = loggerFactory.CreateLogger($"FallbackSceneEncoder[{channelId}]");
    private readonly MediaMtxOptions _mediaMtx = mediaMtxOptions.Value;
    private readonly SceneAssetOptions _assets = assetOptions.Value;
    private Process? _process;
    private Channel? _channel;
    private IReadOnlyList<string> _destinations = Array.Empty<string>();
    private SceneState _currentState = SceneState.Offline;
    private bool _intentionalStop;

    public Guid ChannelId { get; } = channelId;
    public bool IsRunning => _process is { HasExited: false };

    public event Action<Guid, int>? ProcessExitedUnexpectedly;

    public Task StartAsync(Channel channel, IReadOnlyList<string> destinationRtmpUrls, CancellationToken ct = default)
    {
        _channel = channel;
        _destinations = destinationRtmpUrls;
        return RestartWithSourceAsync(SceneState.Offline);
    }

    public Task ApplySceneAsync(SceneState state, TimeSpan? countdown = null, CancellationToken ct = default) =>
        RestartWithSourceAsync(state);

    private Task RestartWithSourceAsync(SceneState state)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException($"Encoder for channel {channelId} was never started.");
        }

        if (state == _currentState && IsRunning)
        {
            return Task.CompletedTask;
        }

        StopProcess();
        _currentState = state;

        var sourcePath = state switch
        {
            SceneState.Live or SceneState.Degraded => $"{_mediaMtx.RtspBaseUrl}/{_mediaMtx.PathPrefix}/{_channel.StreamKey}",
            SceneState.Brb => _assets.BrbVideoPath,
            SceneState.Reconnecting => _assets.ReconnectingVideoPath,
            _ => _assets.OfflineVideoPath
        };

        var isLive = state is SceneState.Live or SceneState.Degraded;
        var args = new List<string> { "-hide_banner", "-loglevel", "warning" };

        if (isLive)
        {
            args.AddRange(new[] { "-rtsp_transport", "tcp", "-i", sourcePath });
        }
        else
        {
            args.AddRange(new[] { "-stream_loop", "-1", "-i", sourcePath });
        }

        args.AddRange(new[]
        {
            "-vf", $"scale={_assets.CanvasWidth}:{_assets.CanvasHeight}",
            "-c:v", "libx264", "-preset", _assets.X264Preset,
            "-b:v", $"{_assets.VideoBitrateKbps}k", "-g", "60",
            "-c:a", "aac", "-b:a", "160k"
        });

        args.AddRange(_destinations.Count switch
        {
            0 => new[] { "-f", "null", "-" },
            1 => new[] { "-f", "flv", _destinations[0] },
            _ => new[] { "-f", "tee", string.Join("|", _destinations.Select(u => $"[f=flv]{u}")) }
        });

        var startInfo = new ProcessStartInfo
        {
            FileName = _assets.FfmpegPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        _intentionalStop = false;
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            if (!_intentionalStop)
            {
                _logger.LogWarning("Fallback compositor for channel {ChannelId} exited unexpectedly with code {Code}", channelId, process.ExitCode);
                ProcessExitedUnexpectedly?.Invoke(channelId, process.ExitCode);
            }
        };
        process.Start();
        process.BeginErrorReadLine();
        _process = process;

        _logger.LogInformation("Restarted fallback compositor for channel {ChannelId} in state {State}", channelId, state);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopProcess();
        return Task.CompletedTask;
    }

    private void StopProcess()
    {
        _intentionalStop = true;
        if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
        }
        _process?.Dispose();
        _process = null;
    }

    public ValueTask DisposeAsync()
    {
        StopProcess();
        return ValueTask.CompletedTask;
    }
}
