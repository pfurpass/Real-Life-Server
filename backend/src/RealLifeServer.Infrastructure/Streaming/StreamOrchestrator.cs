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
    private readonly ConcurrentDictionary<Guid, RestartAttemptState> _restartState = new();

    public async Task EnsureEncoderRunningAsync(Channel channel, IReadOnlyList<StreamDestination> destinations, CancellationToken ct = default)
    {
        if (_encoders.TryGetValue(channel.Id, out var existing) && existing.IsRunning)
        {
            return;
        }

        // Item 10: SceneMonitorHostedService's self-healing check calls this every ~2s whenever
        // the encoder isn't running - without this gate, a persistently failing start (e.g. the
        // reported RTSP-without-a-publisher crash) would retry every single tick forever.
        if (_restartState.TryGetValue(channel.Id, out var attempt))
        {
            var requiredDelay = RestartBackoff.DelayFor(attempt.ConsecutiveFailures);
            var elapsed = DateTimeOffset.UtcNow - attempt.LastAttemptAt;
            if (attempt.ConsecutiveFailures > 0 && elapsed < requiredDelay)
            {
                logger.LogDebug(
                    "Channel {ChannelId}: backing off compositor start ({Elapsed} elapsed of {Required} required after {Failures} consecutive failures)",
                    channel.Id, elapsed, requiredDelay, attempt.ConsecutiveFailures);
                return;
            }
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

        // Order matters here: it becomes the tee muxer's target order in FfmpegCommandBuilder,
        // so DisplayOrder changes need the same restart as adding/removing/toggling a destination.
        var resolvedUrls = destinations
            .Where(d => d.IsEnabled)
            .OrderBy(d => d.DisplayOrder)
            .Select(d => $"{d.RtmpUrl.TrimEnd('/')}/{encryption.Decrypt(d.StreamKeyEncrypted)}")
            .ToList();

        var state = _restartState.GetOrAdd(channel.Id, _ => new RestartAttemptState());
        state.LastAttemptAt = DateTimeOffset.UtcNow;

        try
        {
            await encoder.StartAsync(channel, resolvedUrls, ct);
        }
        catch (Exception ex)
        {
            state.ConsecutiveFailures++;
            logger.LogError(ex,
                "Failed to start compositor for channel {ChannelId} (attempt {Attempt}, next retry no earlier than {Delay})",
                channel.Id, state.ConsecutiveFailures, RestartBackoff.DelayFor(state.ConsecutiveFailures));

            encoder.ProcessExitedUnexpectedly -= OnEncoderCrashed;
            await encoder.DisposeAsync();
            if (_locks.TryRemove(channel.Id, out var failedLease))
            {
                await failedLease.DisposeAsync();
            }
            return; // swallow: a failed compositor start must not break the caller (e.g. the publish auth webhook)
        }

        _encoders[channel.Id] = encoder;
        state.ConsecutiveFailures = 0;
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

        // Counts toward the same backoff gate as a failed StartAsync call, so a compositor that
        // starts successfully but then crashes repeatedly (e.g. every mode-switch restart
        // failing) is throttled too, not just an outright failed start.
        var state = _restartState.GetOrAdd(channelId, _ => new RestartAttemptState());
        state.ConsecutiveFailures++;
        state.LastAttemptAt = DateTimeOffset.UtcNow;

        // Release our own lock immediately so the restart attempt (same node, once backoff
        // allows it) is not blocked behind its own now-stale lease until the TTL expires.
        if (_locks.TryRemove(channelId, out var lease))
        {
            _ = lease.DisposeAsync().AsTask().ContinueWith(
                t => logger.LogWarning(t.Exception, "Failed to release channel lock for {ChannelId} after crash", channelId),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private sealed class RestartAttemptState
    {
        public DateTimeOffset LastAttemptAt;
        public int ConsecutiveFailures;
    }
}
