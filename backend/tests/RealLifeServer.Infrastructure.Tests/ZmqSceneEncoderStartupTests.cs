using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetMQ;
using NetMQ.Sockets;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;
using RealLifeServer.Infrastructure.Streaming;
using Xunit;

namespace RealLifeServer.Infrastructure.Tests;

/// <summary>
/// Exercises ZmqSceneEncoder's real process-spawning and readiness logic (item 3) against tiny
/// local shell-script stand-ins for FFmpeg, so these run without a real FFmpeg or MediaMTX.
/// Linux/macOS only (relies on shebang scripts), matching this project's Docker deployment
/// target. Each test uses its own fixed port to stay parallel-safe.
/// </summary>
public class ZmqSceneEncoderStartupTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private string CreateStubScript(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ffmpeg-stub-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");

        // File.SetUnixFileMode throws PlatformNotSupportedException on Windows (CA1416) - this
        // whole class only makes sense on Unix anyway (shebang scripts, see class summary), so
        // skip straight to a clear failure there instead of silently doing nothing.
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                $"{nameof(ZmqSceneEncoderStartupTests)} spawns shebang shell scripts and only runs on Linux/macOS.");
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _tempFiles.Add(path);
        return path;
    }

    private static FfmpegCommandBuilder CreateBuilder(string ffmpegPath) =>
        new(Options.Create(new MediaMtxOptions()), Options.Create(new SceneAssetOptions { FfmpegPath = ffmpegPath }));

    private static Channel CreateChannel() => new() { Id = Guid.NewGuid(), Name = "Test", StreamKey = "test-key" };

    private sealed class FixedPortAllocator(int port) : IZmqPortAllocator
    {
        public int Allocate(Guid channelId) => port;
        public void Release(Guid channelId)
        {
            // no-op: the test owns the port's lifetime
        }
    }

    [Fact]
    public async Task StartAsync_FfmpegExitsImmediately_ThrowsWithExitCodeAndBufferedFfmpegOutput()
    {
        var stub = CreateStubScript("echo 'no stream is available on path' 1>&2\nexit 1");
        var encoder = new ZmqSceneEncoder(Guid.NewGuid(), CreateBuilder(stub), new FixedPortAllocator(25101), NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => encoder.StartAsync(CreateChannel(), [], CancellationToken.None));

        // Regression guard: this used to be replaced by Process's own
        // "No process is associated with this object." because ExitCode was read after Dispose().
        Assert.Contains("exited during startup", ex.Message);
        Assert.Contains("code 1", ex.Message);
        Assert.Contains("no stream is available on path", ex.Message);
        Assert.False(encoder.IsRunning);
    }

    [Fact]
    public async Task StartAsync_FfmpegExitsBeforeAnyOutput_DoesNotLeakTheRawProcessException()
    {
        // Minimizes the window between process.Start() and the process already being gone -
        // exercises the "exits before _process is meaningfully usable" race directly, without
        // relying on any stderr output existing yet.
        var stub = CreateStubScript("exit 1");
        var encoder = new ZmqSceneEncoder(Guid.NewGuid(), CreateBuilder(stub), new FixedPortAllocator(25104), NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => encoder.StartAsync(CreateChannel(), [], CancellationToken.None));

        Assert.Contains("exited during startup", ex.Message);
        Assert.DoesNotContain("No process is associated", ex.Message);
        Assert.False(encoder.IsRunning);
    }

    [Fact]
    public async Task StartAsync_ExitedEventFiresDuringTheReadinessPoll_DoesNotAlsoRaiseProcessExitedUnexpectedly()
    {
        // The stub survives the initial startup grace period, then exits mid-poll - so the
        // Exited event and the readiness loop's own HasExited check race against each other.
        // A failed *startup* must never additionally surface as a normal runtime crash signal.
        var stub = CreateStubScript("sleep 0.6\nexit 1");
        var encoder = new ZmqSceneEncoder(Guid.NewGuid(), CreateBuilder(stub), new FixedPortAllocator(25105), NullLoggerFactory.Instance);
        var unexpectedCrashSignalled = false;
        encoder.ProcessExitedUnexpectedly += (_, _) => unexpectedCrashSignalled = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => encoder.StartAsync(CreateChannel(), [], CancellationToken.None));

        Assert.Contains("exited during startup", ex.Message);
        Assert.False(unexpectedCrashSignalled, "A failed startup must not also raise ProcessExitedUnexpectedly.");
    }

    [Fact]
    public async Task StartAsync_FfmpegExitsImmediately_DisposeAfterwardsDoesNotThrow()
    {
        var stub = CreateStubScript("exit 1");
        var encoder = new ZmqSceneEncoder(Guid.NewGuid(), CreateBuilder(stub), new FixedPortAllocator(25106), NullLoggerFactory.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => encoder.StartAsync(CreateChannel(), [], CancellationToken.None));

        // The failed process was already disposed internally (item 9: only after the exception
        // was fully built) - disposing the encoder again afterwards must be a safe no-op.
        await encoder.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_ZmqPortNeverAnswers_ThrowsAfterReadinessWindowAndKillsTheProcess()
    {
        var stub = CreateStubScript("sleep 30");
        var encoder = new ZmqSceneEncoder(Guid.NewGuid(), CreateBuilder(stub), new FixedPortAllocator(25102), NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => encoder.StartAsync(CreateChannel(), [], CancellationToken.None));

        Assert.Contains("never became ready", ex.Message);
        Assert.False(encoder.IsRunning);
    }

    [Fact]
    public async Task StartAsync_ProcessAliveAndZmqResponds_BecomesReady()
    {
        const int port = 25103;
        var stub = CreateStubScript("sleep 30"); // stands in for a live ffmpeg process; the fake responder below stands in for its zmq filter
        using var responder = new ResponseSocket();
        responder.Bind($"tcp://127.0.0.1:{port}");
        using var responderStop = new CancellationTokenSource();

        var responderTask = Task.Run(() =>
        {
            while (!responderStop.IsCancellationRequested)
            {
                if (responder.TryReceiveFrameString(TimeSpan.FromMilliseconds(200), out _))
                {
                    responder.SendFrame("0 Success");
                }
            }
        });

        var encoder = new ZmqSceneEncoder(Guid.NewGuid(), CreateBuilder(stub), new FixedPortAllocator(port), NullLoggerFactory.Instance);
        try
        {
            await encoder.StartAsync(CreateChannel(), [], CancellationToken.None);
            Assert.True(encoder.IsRunning);
        }
        finally
        {
            await encoder.StopAsync(CancellationToken.None);
            responderStop.Cancel();
            await responderTask;
        }
    }
}
