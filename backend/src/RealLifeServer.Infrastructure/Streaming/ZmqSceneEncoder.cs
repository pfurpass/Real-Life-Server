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
    /// <summary>
    /// Explicit lifecycle for the currently-tracked <see cref="Process"/>, so the Exited event
    /// handler can tell "this is a genuine unexpected runtime crash" apart from "this process is
    /// exiting because we're in the middle of starting or intentionally stopping it" without
    /// racing against *when* Exited happens to fire. The state is always set *before* the action
    /// that could trigger Exited (Start/Kill), never inferred from event timing - that is what
    /// makes the check race-free: Exited only ever reads a value that was already stable before
    /// the OS-level exit could possibly have happened.
    /// </summary>
    private enum LifecycleState { Starting, Ready, Stopping, Exited }

    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Redirected-stream callbacks (ErrorDataReceived) are not guaranteed to have all fired by
    /// the time WaitForExitAsync returns - this bounds how long a failed startup waits for the
    /// last buffered stderr lines before building the failure message from whatever arrived.
    /// </summary>
    private static readonly TimeSpan StderrDrainGracePeriod = TimeSpan.FromMilliseconds(100);

    private const int MaxBufferedStderrLines = 40;

    private readonly ILogger _logger = loggerFactory.CreateLogger($"SceneEncoder[{channelId}]");
    private readonly object _stderrLock = new();
    private readonly List<string> _stderrTail = [];
    private readonly object _lifecycleLock = new();

    private LifecycleState _lifecycleState = LifecycleState.Exited;
    private Process? _process;
    private ZmqFilterController? _zmq;
    private CompositorPlan? _plan;

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
        SetLifecycleState(LifecycleState.Starting);

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

            // Only report a crash if this process was previously confirmed Ready. An exit while
            // Starting is handled synchronously by the readiness loop below (it throws a
            // descriptive exception); an exit while Stopping/Exited is intentional (StopAsync,
            // a mode-switch restart, or FailRunningAsync already killed this process on
            // purpose). Reading a state set *before* Start()/Kill() - not a flag raced against
            // this event's arrival time - is what makes this check safe regardless of how
            // quickly the process exits.
            if (GetLifecycleState() == LifecycleState.Ready)
            {
                var code = SafeReadExitCode(process) ?? -1;
                _logger.LogWarning("Compositor for channel {ChannelId} exited unexpectedly with code {Code}", channelId, code);
                ProcessExitedUnexpectedly?.Invoke(channelId, code);
            }
        };

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
                SetLifecycleState(LifecycleState.Ready);
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

    /// <summary>
    /// Terminates <paramref name="process"/> (if still alive) and builds a descriptive
    /// exception, then disposes it. Order matters: exit code and stderr are captured *before*
    /// Dispose() - Process throws InvalidOperationException("No process is associated with this
    /// object.") from ExitCode (and most other members) once disposed, which previously replaced
    /// the intended failure message with that unhelpful one.
    /// </summary>
    private async Task<InvalidOperationException> FailStartupAsync(Process process, ZmqFilterController? zmq, string? overrideMessage = null, Exception? innerException = null)
    {
        SetLifecycleState(LifecycleState.Stopping);

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

        // Give any already-in-flight ErrorDataReceived callbacks a moment to finish appending
        // to the stderr buffer before we read it - see StderrDrainGracePeriod.
        await Task.Delay(StderrDrainGracePeriod, CancellationToken.None);

        int? exitCode = SafeReadExitCode(process);
        var tail = GetStderrTail();

        if (zmq is not null)
        {
            await zmq.DisposeAsync();
        }
        process.Dispose();
        _process = null;
        SetLifecycleState(LifecycleState.Exited);

        var message = overrideMessage ?? (exitCode is { } code
            ? $"Compositor for channel {channelId} exited during startup with code {code}."
            : $"Compositor for channel {channelId} exited during startup, but its exit code could not be determined.");
        if (tail.Length > 0)
        {
            message += $" FFmpeg output:\n{tail}";
        }

        return new InvalidOperationException(message, innerException);
    }

    /// <summary>Never lets Process's raw InvalidOperationException ("No process is associated with this object.") escape from an ExitCode read - item 8.</summary>
    private static int? SafeReadExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
        SetLifecycleState(LifecycleState.Stopping);
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
        SetLifecycleState(LifecycleState.Exited);
        ProcessExitedUnexpectedly?.Invoke(channelId, -1);
    }

    private async Task StopCurrentProcessAsync(CancellationToken ct)
    {
        SetLifecycleState(LifecycleState.Stopping);
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
        // immediate Allocate call right after this returns is idempotent per channel and
        // typically observes the allocation still cached (Exited hasn't necessarily run yet),
        // so the replacement process keeps the same port. Even in the rare case another
        // channel's Allocate call races in between, this channel simply gets handed a different
        // free port - the plan returned by BuildZmqCompositor is always used fresh, nothing
        // assumes port stability.
        await DisposeAsync();
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        SetLifecycleState(LifecycleState.Stopping);
        if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct);
        }

        portAllocator.Release(channelId);
        await DisposeAsync();
        SetLifecycleState(LifecycleState.Exited);
    }

    private void SetLifecycleState(LifecycleState state)
    {
        lock (_lifecycleLock)
        {
            _lifecycleState = state;
        }
    }

    private LifecycleState GetLifecycleState()
    {
        lock (_lifecycleLock)
        {
            return _lifecycleState;
        }
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
