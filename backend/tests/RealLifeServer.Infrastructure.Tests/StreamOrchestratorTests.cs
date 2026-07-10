using Microsoft.Extensions.Logging.Abstractions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;
using RealLifeServer.Infrastructure.Caching;
using RealLifeServer.Infrastructure.Streaming;
using Xunit;

namespace RealLifeServer.Infrastructure.Tests;

/// <summary>
/// Covers item 10 (restart backoff) and the crash-recovery bookkeeping around
/// <see cref="ISceneEncoder"/>, using a controllable fake instead of real FFmpeg - the encoder's
/// own process/readiness behavior is covered separately by ZmqSceneEncoderStartupTests.
/// </summary>
public class StreamOrchestratorTests
{
    private static Channel CreateChannel() => new() { Id = Guid.NewGuid(), Name = "Test", StreamKey = "key" };

    private static StreamOrchestrator CreateSut(FakeSceneEncoderFactory factory) =>
        new(factory, new NoOpEncryptionService(), new NullChannelLockProvider(), NullLogger<StreamOrchestrator>.Instance);

    [Fact]
    public async Task EnsureEncoderRunningAsync_SuccessfulStart_RegistersTheEncoderAsRunning()
    {
        var factory = new FakeSceneEncoderFactory();
        var sut = CreateSut(factory);
        var channel = CreateChannel();

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);

        Assert.True(sut.IsEncoderRunning(channel.Id));
        Assert.Equal(1, factory.CreateCallCount);
    }

    [Fact]
    public async Task EnsureEncoderRunningAsync_StartThrows_SwallowsExceptionAndLeavesEncoderNotRunning()
    {
        var factory = new FakeSceneEncoderFactory { NextStartFailure = new InvalidOperationException("no stream is available on path") };
        var sut = CreateSut(factory);
        var channel = CreateChannel();

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None); // must not throw

        Assert.False(sut.IsEncoderRunning(channel.Id));
    }

    [Fact]
    public async Task EnsureEncoderRunningAsync_AfterFailedStart_DoesNotRetryImmediately()
    {
        // Item 10: this is the exact reported bug - SceneMonitorHostedService calls
        // EnsureEncoderRunningAsync roughly every 2 seconds whenever the encoder isn't running.
        // Without backoff, a persistently-failing start retries every single tick forever.
        var factory = new FakeSceneEncoderFactory { NextStartFailure = new InvalidOperationException("boom") };
        var sut = CreateSut(factory);
        var channel = CreateChannel();

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);
        Assert.Equal(1, factory.CreateCallCount);

        // Simulate the next monitor tick firing immediately after - must be a no-op.
        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);

        Assert.Equal(1, factory.CreateCallCount);
        Assert.False(sut.IsEncoderRunning(channel.Id));
    }

    [Fact]
    public async Task EnsureEncoderRunningAsync_OnceBackoffElapses_RetriesAndCanSucceed()
    {
        var factory = new FakeSceneEncoderFactory { NextStartFailure = new InvalidOperationException("boom") };
        var sut = CreateSut(factory);
        var channel = CreateChannel();

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);
        Assert.Equal(1, factory.CreateCallCount);

        // First backoff delay is 2s (RestartBackoffTests covers the schedule itself in detail).
        await Task.Delay(TimeSpan.FromSeconds(2.2));

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);

        Assert.Equal(2, factory.CreateCallCount);
        Assert.True(sut.IsEncoderRunning(channel.Id));
    }

    [Fact]
    public async Task Crash_RemovesTheEncoderFromTracking_SoIsEncoderRunningReturnsFalse()
    {
        var factory = new FakeSceneEncoderFactory();
        var sut = CreateSut(factory);
        var channel = CreateChannel();

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);
        Assert.True(sut.IsEncoderRunning(channel.Id));

        factory.LastCreated!.SimulateCrash(1);

        Assert.False(sut.IsEncoderRunning(channel.Id));
    }

    [Fact]
    public async Task CrashAfterSuccessfulStart_AlsoBacksOffTheNextAttempt()
    {
        // A start that succeeds but then crashes moments later must be throttled too - not just
        // an outright failed StartAsync call.
        var factory = new FakeSceneEncoderFactory();
        var sut = CreateSut(factory);
        var channel = CreateChannel();

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);
        factory.LastCreated!.SimulateCrash(1);

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);

        Assert.Equal(1, factory.CreateCallCount); // second attempt was deferred by backoff
        Assert.False(sut.IsEncoderRunning(channel.Id));
    }

    [Fact]
    public async Task StopEncoderAsync_RemovesAndStopsTheEncoder()
    {
        var factory = new FakeSceneEncoderFactory();
        var sut = CreateSut(factory);
        var channel = CreateChannel();

        await sut.EnsureEncoderRunningAsync(channel, [], CancellationToken.None);
        await sut.StopEncoderAsync(channel.Id, CancellationToken.None);

        Assert.False(sut.IsEncoderRunning(channel.Id));
        Assert.Equal(1, factory.LastCreated!.StopCallCount);
    }

    private sealed class NoOpEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }

    private sealed class FakeSceneEncoder(Guid channelId) : ISceneEncoder
    {
        public Exception? FailStartWith { get; set; }
        public int StopCallCount { get; private set; }

        public Guid ChannelId => channelId;
        public bool IsRunning { get; private set; }

        public event Action<Guid, int>? ProcessExitedUnexpectedly;

        public Task StartAsync(Channel channel, IReadOnlyList<string> destinationRtmpUrls, CancellationToken ct = default)
        {
            if (FailStartWith is { } ex)
            {
                throw ex;
            }
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task ApplySceneAsync(SceneState state, TimeSpan? countdown = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCallCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void SimulateCrash(int exitCode)
        {
            IsRunning = false;
            ProcessExitedUnexpectedly?.Invoke(channelId, exitCode);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSceneEncoderFactory : ISceneEncoderFactory
    {
        public Exception? NextStartFailure { get; set; }
        public FakeSceneEncoder? LastCreated { get; private set; }
        public int CreateCallCount { get; private set; }

        public ISceneEncoder Create(Channel channel)
        {
            CreateCallCount++;
            var encoder = new FakeSceneEncoder(channel.Id) { FailStartWith = NextStartFailure };
            NextStartFailure = null;
            LastCreated = encoder;
            return encoder;
        }
    }
}
