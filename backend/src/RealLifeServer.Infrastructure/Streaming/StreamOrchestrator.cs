using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Owns one <see cref="ISceneEncoder"/> per active channel on this media node. See
/// docs/CONCEPT.md, chapter 11.2 for how channels are pinned to a single node when scaling
/// horizontally across multiple media nodes.
/// </summary>
public class StreamOrchestrator(
    ISceneEncoderFactory encoderFactory,
    IEncryptionService encryption,
    IChannelLockProvider lockProvider,
    ILogger<StreamOrchestrator> logger) : IStreamOrchestrator
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, ISceneEncoder> _encoders = new();
    private readonly ConcurrentDictionary<Guid, IAsyncDisposable> _locks = new();

    public async Task EnsureEncoderRunningAsync(Channel channel, IReadOnlyList<StreamDestination> destinations, CancellationToken ct = default)
    {
        if (_encoders.TryGetValue(channel.Id, out var existing) && existing.IsRunning)
        {
            return;
        }

        // Only one media node may run this channel's compositor at a time (docs/CONCEPT.md,
        // chapter 11.2). If another node already holds the lock, back off - that node owns it.
        var lease = await lockProvider.TryAcquireAsync(channel.Id, LockTtl, ct);
        if (lease is null)
        {
            logger.LogInformation("Channel {ChannelId} is owned by another media node, not starting a local compositor", channel.Id);
            return;
        }
        _locks[channel.Id] = lease;

        var encoder = encoderFactory.Create(channel);
        encoder.ProcessExitedUnexpectedly += OnEncoderCrashed;

        var resolvedUrls = destinations
            .Where(d => d.IsEnabled)
            .Select(d => $"{d.RtmpUrl.TrimEnd('/')}/{encryption.Decrypt(d.StreamKeyEncrypted)}")
            .ToList();

        await encoder.StartAsync(channel, resolvedUrls, ct);
        _encoders[channel.Id] = encoder;
    }

    public async Task ApplySceneAsync(Guid channelId, SceneState state, TimeSpan? countdown = null, CancellationToken ct = default)
    {
        if (!_encoders.TryGetValue(channelId, out var encoder) || !encoder.IsRunning)
        {
            logger.LogWarning("ApplySceneAsync({ChannelId}, {State}) called but no running encoder was found - ignoring until the next monitor tick restarts it.", channelId, state);
            return;
        }

        await encoder.ApplySceneAsync(state, countdown, ct);
    }

    public async Task StopEncoderAsync(Guid channelId, CancellationToken ct = default)
    {
        if (_encoders.TryRemove(channelId, out var encoder))
        {
            encoder.ProcessExitedUnexpectedly -= OnEncoderCrashed;
            await encoder.StopAsync(ct);
            await encoder.DisposeAsync();
        }

        if (_locks.TryRemove(channelId, out var lease))
        {
            await lease.DisposeAsync();
        }
    }

    public bool IsEncoderRunning(Guid channelId) =>
        _encoders.TryGetValue(channelId, out var encoder) && encoder.IsRunning;

    private void OnEncoderCrashed(Guid channelId, int exitCode)
    {
        logger.LogCritical("Compositor process for channel {ChannelId} crashed with exit code {ExitCode}. It will be restarted on the next monitor tick.", channelId, exitCode);
        _encoders.TryRemove(channelId, out _);

        // Release our own lock immediately so the restart attempt (same node, next monitor
        // tick) is not blocked behind its own now-stale lease until the TTL expires.
        if (_locks.TryRemove(channelId, out var lease))
        {
            _ = lease.DisposeAsync().AsTask().ContinueWith(
                t => logger.LogWarning(t.Exception, "Failed to release channel lock for {ChannelId} after crash", channelId),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
