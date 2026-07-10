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
    public async Task StartAsync_FfmpegExitsImmediately_ThrowsWithBufferedFfmpegOutput()
    {
        var stub = CreateStubScript("echo 'no stream is available on path' 1>&2\nexit 1");
        var encoder = new ZmqSceneEncoder(Guid.NewGuid(), CreateBuilder(stub), new FixedPortAllocator(25101), NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => encoder.StartAsync(CreateChannel(), [], CancellationToken.None));

        Assert.Contains("exited during startup", ex.Message);
        Assert.Contains("no stream is available on path", ex.Message);
        Assert.False(encoder.IsRunning);
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
