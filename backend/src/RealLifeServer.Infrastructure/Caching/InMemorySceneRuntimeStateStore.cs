using System.Collections.Concurrent;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.StateMachine;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Caching;

/// <summary>
/// Process-local implementation of <see cref="ISceneRuntimeStateStore"/>. Sufficient because
/// every channel's monitoring loop runs on exactly one media node at a time (docs/CONCEPT.md,
/// chapter 11.2) - there is no cross-instance state to reconcile. A Redis-backed
/// implementation would be a drop-in replacement if that assumption ever changed.
/// </summary>
public class InMemorySceneRuntimeStateStore : ISceneRuntimeStateStore
{
    private sealed class ChannelRuntimeState
    {
        public DateTimeOffset StateEnteredAt = DateTimeOffset.UtcNow;
        public int ConsecutiveGoodSamples;
        public DateTimeOffset? LastHeartbeatAt;
    }

    private readonly ConcurrentDictionary<Guid, ChannelRuntimeState> _state = new();

    private ChannelRuntimeState Get(Guid channelId) => _state.GetOrAdd(channelId, _ => new ChannelRuntimeState());

    public SceneContext GetContext(Guid channelId)
    {
        var state = Get(channelId);
        return new SceneContext(DateTimeOffset.UtcNow - state.StateEnteredAt, state.ConsecutiveGoodSamples);
    }

    public void RecordStateEntered(Guid channelId, SceneState state, DateTimeOffset enteredAt) =>
        Get(channelId).StateEnteredAt = enteredAt;

    public void RecordGoodSample(Guid channelId) =>
        Interlocked.Increment(ref Get(channelId).ConsecutiveGoodSamples);

    public void ResetGoodSamples(Guid channelId) =>
        Get(channelId).ConsecutiveGoodSamples = 0;

    public void RecordLastHeartbeat(Guid channelId, DateTimeOffset at) =>
        Get(channelId).LastHeartbeatAt = at;

    public DateTimeOffset? GetLastHeartbeat(Guid channelId) => Get(channelId).LastHeartbeatAt;
}
