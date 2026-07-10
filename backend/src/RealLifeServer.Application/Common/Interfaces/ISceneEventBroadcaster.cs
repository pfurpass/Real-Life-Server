using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>
/// Push abstraction toward connected dashboard clients. Implemented in the Api layer via
/// SignalR so Application/Infrastructure never reference ASP.NET Core hub types directly.
/// </summary>
public interface ISceneEventBroadcaster
{
    Task BroadcastSceneChangedAsync(Guid channelId, SceneState fromState, SceneState toState, SceneTrigger trigger, DateTimeOffset occurredAt);

    Task BroadcastMetricsUpdatedAsync(Guid channelId, int bitrateKbps, double packetLossPercent, double fpsIn, DateTimeOffset timestamp);

    Task BroadcastLogAppendedAsync(Guid channelId, string level, string message, DateTimeOffset timestamp);
}
