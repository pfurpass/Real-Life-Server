using RealLifeServer.Application.Streams.StateMachine;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>
/// Holds the transient counters the state machine needs (time-in-state, consecutive good
/// samples) that are not worth persisting to the database. One instance's worth of channels
/// live on a single media node (docs/CONCEPT.md, chapter 11.2), so an in-memory implementation
/// is sufficient; a Redis-backed implementation would be a drop-in replacement if orchestration
/// itself ever became distributed per channel.
/// </summary>
public interface ISceneRuntimeStateStore
{
    SceneContext GetContext(Guid channelId);
    void RecordStateEntered(Guid channelId, SceneState state, DateTimeOffset enteredAt);
    void RecordGoodSample(Guid channelId);
    void ResetGoodSamples(Guid channelId);
    void RecordLastHeartbeat(Guid channelId, DateTimeOffset at);
    DateTimeOffset? GetLastHeartbeat(Guid channelId);
}
