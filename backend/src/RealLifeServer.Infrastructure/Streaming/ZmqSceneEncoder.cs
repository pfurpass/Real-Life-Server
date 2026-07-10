using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Manages the FFmpeg compositor process for a channel using the Zmq compositor strategy (see
/// docs/CONCEPT.md chapter 4 and 4.1).
///
/// The process runs in one of two mutually exclusive modes (<see cref="RequiresLiveInput"/>):
///  - <b>NoLiveInput</b> (Offline, Connecting, Reconnecting, Brb): never opens the RTSP live
///    feed at all - only a trivial, always-available filler input. Runs indefinitely with no
///    mobile encoder connected.
///  - <b>LiveInput</b> (Live, Degraded): opens the RTSP live feed. Only entered once a publisher
///    is already confirmed present, because MediaMTX does not let a reader wait for an absent
///    one - it fails the connection immediately with "no stream is available on path" instead.
///
/// Switching *within* a mode (e.g. Reconnecting -&gt; Brb, or Live -&gt; Degraded) is a runtime
/// zmq command - no process restart, no interruption to the outbound RTMP connection. Switching
/// *between* modes requires a full process restart, because FFmpeg's filter graph is static once
/// built and cannot gain or lose an input via zmq. Restart attempts are backoff-limited
/// (<see cref="RestartBackoff"/>) so a flapping mobile connection cannot spin this in a tight
/// loop against the outbound Twitch/YouTube connection.
/// </summary>
public sealed class ZmqSceneEncoder(
    Guid channelId,
    FfmpegCommandBuilder commandBuilder,
    IZmqPortAllocator portAllocator,
    ILoggerFactory loggerFactory) : ISceneEncoder
{
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromMilliseconds(250);
    private const int MaxBufferedStderrLines = 40;

    private readonly ILogger _logger = loggerFactory.CreateLogger($"SceneEncoder[{channelId}]");
    private readonly object _stderrLock = new();
    private readonly List<string> _stderrTail = [];

    private Process? _process;
    private ZmqFilterController? _zmq;
    private CompositorPlan? _plan;
    private bool _intentionalStop;

    private Channel? _channel;
    private IReadOnlyList<string>? _destinationRtmpUrls;

    private DateTimeOffset _nextModeSwitchAttemptAt = DateTimeOffset.MinValue;
    private int _consecutiveModeSwitchFailures;

    public Guid ChannelId => channelId;
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Raised when the compositor is now dead and needs to be fully recreated by the orchestrator - a genuine process crash, or a mode-switch restart that failed after exhausting its own readiness check.</summary>
    public event Action<Guid, int>? ProcessExitedUnexpectedly;

    internal static bool RequiresLiveInput(SceneState state) => state is SceneState.Live or SceneState.Degraded;

    public async Task StartAsync(Channel channel, IReadOnlyList<string> destinationRtmpUrls, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            return;
        }

        _channel = channel;
        _destinationRtmpUrls = destinationRtmpUrls;

        // Always starts in NoLiveInput mode: at the moment a channel first starts (or is
        // recreated after a crash) the caller has not yet told us the target scene state, and
        // opening the RTSP input speculatively risks the exact "no stream is available" crash
        // this whole class exists to avoid. The caller's very next call is always
        // ApplySceneAsync(actualState, ...), which switches to LiveInput if that state needs it.
        var port = portAllocator.Allocate(channelId);
        var plan = commandBuilder.BuildZmqCompositor(channel, destinationRtmpUrls, port, includeLiveInput: false);
        await StartProcessAsync(plan, ct);
    }

    public async Task ApplySceneAsync(SceneState state, TimeSpan? countdown = null, CancellationToken ct = default)
    {
        if (_plan is null)
        {
            throw new InvalidOperationException($"Compositor for channel {channelId} was never started.");
        }

        var needsLiveInput = RequiresLiveInput(state);
        if (needsLiveInput != _plan.HasLiveInput && !await TrySwitchModeAsync(needsLiveInput, ct))
        {
            return; // already logged; this instance is now dead and the orchestrator has been notified
        }

        if (_zmq is null || _process is not { HasExited: false })
        {
            _logger.LogWarning("Channel {ChannelId}: skipping scene update to {State} - compositor is not running", channelId, state);
            return;
        }

        var (brb, reconnecting, offline) = state switch
        {
            SceneState.Brb => (true, false, false),
            SceneState.Reconnecting => (false, true, false),
            SceneState.Live or SceneState.Degraded => (false, false, false),
            _ => (false, false, true) // Offline, Connecting
        };

        if (!await TrySendOverlayStateAsync(brb, reconnecting, offline, ct))
        {
            return;
        }

        if (state == SceneState.Reconnecting && countdown is not null)
        {
            var seconds = Math.Max(0, (int)countdown.Value.TotalSeconds);
            await TrySendCountdownAsync(seconds, ct);
        }
    }

    /// <summary>Restarts the process with the other input mode. Backoff-gated: a failed switch does not retry on every subsequent call, only once the required delay has elapsed.</summary>
    private async Task<bool> TrySwitchModeAsync(bool needsLiveInput, CancellationToken ct)
    {
        var modeName = needsLiveInput ? "LiveInput" : "NoLiveInput";

        if (DateTimeOffset.UtcNow < _nextModeSwitchAttemptAt)
        {
            _logger.LogDebug(
                "Channel {ChannelId}: mode-switch to {Mode} deferred until {NextAttempt} (backoff after {Failures} consecutive failures)",
                channelId, modeName, _nextModeSwitchAttemptAt, _consecutiveModeSwitchFailures);
            return false;
        }

        _logger.LogInformation(
            "Channel {ChannelId}: switching compositor to {Mode} mode - this requires a process restart (FFmpeg cannot hot-swap inputs), so the outbound connection will briefly reconnect",
            channelId, modeName);

        await StopCurrentProcessAsync(ct);

        try
        {
            var port = portAllocator.Allocate(channelId);
            var plan = commandBuilder.BuildZmqCompositor(_channel!, _destinationRtmpUrls!, port, needsLiveInput);
            await StartProcessAsync(plan, ct);
            _consecutiveModeSwitchFailures = 0;
            return true;
        }
        catch (Exception ex)
        {
            _consecutiveModeSwitchFailures++;
            var delay = RestartBackoff.DelayFor(_consecutiveModeSwitchFailures);
            _nextModeSwitchAttemptAt = DateTimeOffset.UtcNow + delay;
            _logger.LogError(ex,
                "Channel {ChannelId}: mode-switch to {Mode} failed (attempt {Attempt}); next attempt no earlier than {Delay}",
                channelId, modeName, _consecutiveModeSwitchFailures, delay);

            // This instance is now dead. Signal it the same way a genuine crash would, so the
            // orchestrator recreates it from scratch on the next monitor tick (respecting its
            // own backoff too).
            ProcessExitedUnexpectedly?.Invoke(channelId, -1);
            return false;
        }
    }

    /// <summary>Starts <paramref name="plan"/> and blocks until it is confirmed ready (process alive AND zmq responding), or throws with the buffered FFmpeg output.</summary>
    private async Task StartProcessAsync(CompositorPlan plan, CancellationToken ct)
    {
        _plan = plan;
        ClearStderrTail();

        _logger.LogInformation(
            "Channel {ChannelId}: starting compositor ({Mode} mode) with command: {Command} {Arguments}",
            channelId, plan.HasLiveInput ? "LiveInput" : "NoLiveInput", plan.Executable, FfmpegArgumentMasking.ToLoggableString(plan.Arguments));

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
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }
            // Information, not Debug: this is the only place FFmpeg's actual error output
            // (e.g. "no stream is available on path") is visible, and it matters in production.
            _logger.LogInformation("ffmpeg[{ChannelId}]: {Line}", channelId, e.Data);
            AppendStderrLine(e.Data);
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

        await Task.Delay(StartupGracePeriod, ct);
        if (process.HasExited)
        {
            throw await FailStartupAsync(process, zmq: null);
        }

        var zmq = new ZmqFilterController(plan.ZmqPort, _logger);
        var deadline = DateTimeOffset.UtcNow + ReadinessTimeout;
        Exception? lastProbeError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw await FailStartupAsync(process, zmq);
            }

            try
            {
                // Re-asserting the graph's own already-correct default state doubles as the
                // readiness probe: it only succeeds once FFmpeg has finished building the filter
                // graph and the zmq filter is actually accepting commands (item 3).
                await zmq.SetOverlayEnabledAsync(plan.OfflineOverlayName, !plan.HasLiveInput, ct);
                _zmq = zmq;
                _logger.LogInformation("Compositor for channel {ChannelId} is ready (zmq port {Port})", channelId, plan.ZmqPort);
                return;
            }
            catch (Exception ex)
            {
                lastProbeError = ex;
                await Task.Delay(ReadinessPollInterval, ct);
            }
        }

        if (process.HasExited)
        {
            throw await FailStartupAsync(process, zmq);
        }

        // Process is alive but its zmq control socket never answered - the filter graph may not
        // have initialized correctly. Not "normal control flow": this is a real failure.
        var timeoutMessage =
            $"Compositor for channel {channelId} started but its zmq control socket on port {plan.ZmqPort} " +
            $"never became ready within {ReadinessTimeout.TotalSeconds}s.";
        throw await FailStartupAsync(process, zmq, timeoutMessage, lastProbeError);
    }

    private async Task<InvalidOperationException> FailStartupAsync(Process process, ZmqFilterController? zmq, string? overrideMessage = null, Exception? innerException = null)
    {
        _intentionalStop = true; // suppress the Exited handler's own duplicate crash signal - we surface this failure via the thrown exception instead

        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // best effort - we're already failing startup
            }
        }

        if (zmq is not null)
        {
            await zmq.DisposeAsync();
        }

        process.Dispose();
        _process = null;

        var tail = GetStderrTail();
        var message = overrideMessage ??
            $"Compositor for channel {channelId} exited during startup with code {process.ExitCode}.";
        if (tail.Length > 0)
        {
            message += $" FFmpeg output:\n{tail}";
        }

        return new InvalidOperationException(message, innerException);
    }

    private async Task<bool> TrySendOverlayStateAsync(bool brb, bool reconnecting, bool offline, CancellationToken ct)
    {
        try
        {
            await _zmq!.SetOverlayEnabledAsync(_plan!.BrbOverlayName, brb, ct);
            await _zmq.SetOverlayEnabledAsync(_plan.ReconnectingOverlayName, reconnecting, ct);
            await _zmq.SetOverlayEnabledAsync(_plan.OfflineOverlayName, offline, ct);
            return true;
        }
        catch (Exception ex)
        {
            // A zmq failure during otherwise-normal operation means the compositor is in a bad
            // state (hung, filter graph broken) - not something to silently retry forever.
            // Treat it exactly like a crash: kill it and let the orchestrator recreate it.
            _logger.LogError(ex, "Channel {ChannelId}: zmq scene update failed - treating compositor as dead", channelId);
            await FailRunningAsync(ct);
            return false;
        }
    }

    private async Task TrySendCountdownAsync(int seconds, CancellationToken ct)
    {
        try
        {
            await _zmq!.UpdateCountdownTextAsync(_plan!.CountdownTextName, $"Verbindung wird wiederhergestellt… {seconds}s", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Channel {ChannelId}: zmq countdown update failed - treating compositor as dead", channelId);
            await FailRunningAsync(ct);
        }
    }

    private async Task FailRunningAsync(CancellationToken ct)
    {
        _intentionalStop = true;
        if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch
            {
                // best effort
            }
        }
        portAllocator.Release(channelId);
        await DisposeAsync();
        ProcessExitedUnexpectedly?.Invoke(channelId, -1);
    }

    private async Task StopCurrentProcessAsync(CancellationToken ct)
    {
        _intentionalStop = true;
        if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch
            {
                // best effort during a controlled restart
            }
        }
        // The killed process's own Exited handler releases the port asynchronously; the
        // immediate Allocate call below is idempotent per channel and typically observes the
        // allocation still cached (Exited hasn't necessarily run yet), so the replacement
        // process keeps the same port. Even in the rare case another channel's Allocate call
        // races in between, this channel simply gets handed a different free port - the plan
        // returned by BuildZmqCompositor is always used fresh, nothing assumes port stability.
        await DisposeAsync();
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

    private void AppendStderrLine(string line)
    {
        lock (_stderrLock)
        {
            _stderrTail.Add(line);
            if (_stderrTail.Count > MaxBufferedStderrLines)
            {
                _stderrTail.RemoveAt(0);
            }
        }
    }

    private string GetStderrTail()
    {
        lock (_stderrLock)
        {
            return string.Join('\n', _stderrTail);
        }
    }

    private void ClearStderrTail()
    {
        lock (_stderrLock)
        {
            _stderrTail.Clear();
        }
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
