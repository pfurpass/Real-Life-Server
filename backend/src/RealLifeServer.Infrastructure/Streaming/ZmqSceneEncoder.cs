using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Manages one persistent FFmpeg compositor process for a channel using the Zmq compositor
/// strategy (docs/CONCEPT.md chapter 4). Scene switches are runtime zmq commands, not process
/// restarts, so the outbound RTMP connection to the target platform(s) is never interrupted.
/// </summary>
public sealed class ZmqSceneEncoder(
    Guid channelId,
    FfmpegCommandBuilder commandBuilder,
    IZmqPortAllocator portAllocator,
    ILoggerFactory loggerFactory) : ISceneEncoder
{
    private readonly ILogger _logger = loggerFactory.CreateLogger($"SceneEncoder[{channelId}]");
    private Process? _process;
    private ZmqFilterController? _zmq;
    private CompositorPlan? _plan;
    private bool _intentionalStop;

    public Guid ChannelId => channelId;
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Raised when the FFmpeg process exits unexpectedly (crash, killed) so the orchestrator can react.</summary>
    public event Action<Guid, int>? ProcessExitedUnexpectedly;

    public Task StartAsync(Channel channel, IReadOnlyList<string> destinationRtmpUrls, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        var port = portAllocator.Allocate(channelId);
        var plan = commandBuilder.BuildZmqCompositor(channel, destinationRtmpUrls, port);
        _plan = plan;

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.Executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in plan.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _logger.LogDebug("ffmpeg[{ChannelId}]: {Line}", channelId, e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            portAllocator.Release(channelId);
            if (!_intentionalStop)
            {
                var code = process.ExitCode;
                _logger.LogWarning("Compositor for channel {ChannelId} exited unexpectedly with code {Code}", channelId, code);
                ProcessExitedUnexpectedly?.Invoke(channelId, code);
            }
        };

        _intentionalStop = false;
        process.Start();
        process.BeginErrorReadLine();

        _process = process;
        _zmq = new ZmqFilterController(port, _logger);

        _logger.LogInformation("Started compositor for channel {ChannelId} on zmq port {Port}", channelId, port);
        return Task.CompletedTask;
    }

    public async Task ApplySceneAsync(SceneState state, TimeSpan? countdown = null, CancellationToken ct = default)
    {
        if (_zmq is null || _plan is null)
        {
            throw new InvalidOperationException($"Compositor for channel {channelId} is not running.");
        }

        var plan = _plan;
        var (live, brb, reconnecting, offline) = state switch
        {
            SceneState.Live or SceneState.Degraded => (true, false, false, false),
            SceneState.Brb => (false, true, false, false),
            SceneState.Reconnecting => (false, false, true, false),
            _ => (false, false, false, true) // Offline, Connecting
        };

        await _zmq.SetOverlayEnabledAsync(plan.LiveOverlayName, live, ct);
        await _zmq.SetOverlayEnabledAsync(plan.BrbOverlayName, brb, ct);
        await _zmq.SetOverlayEnabledAsync(plan.ReconnectingOverlayName, reconnecting, ct);
        await _zmq.SetOverlayEnabledAsync(plan.OfflineOverlayName, offline, ct);

        if (state == SceneState.Reconnecting && countdown is not null)
        {
            var seconds = Math.Max(0, (int)countdown.Value.TotalSeconds);
            await _zmq.UpdateCountdownTextAsync(plan.CountdownTextName, $"Verbindung wird wiederhergestellt… {seconds}s", ct);
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _intentionalStop = true;
        if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct);
        }

        portAllocator.Release(channelId);
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_zmq is not null)
        {
            await _zmq.DisposeAsync();
            _zmq = null;
        }
        _process?.Dispose();
        _process = null;
    }
}
