using Microsoft.AspNetCore.SignalR;
using RealLifeServer.Api.Hubs;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Api.Realtime;

/// <summary>
/// SignalR implementation of the Application-layer broadcast abstraction. Kept in the Api
/// project (the composition root) so Application/Infrastructure never reference ASP.NET Core
/// SignalR types directly.
/// </summary>
public class HubSceneEventBroadcaster(IHubContext<StreamHub> hub) : ISceneEventBroadcaster
{
    public Task BroadcastSceneChangedAsync(Guid channelId, SceneState fromState, SceneState toState, SceneTrigger trigger, DateTimeOffset occurredAt) =>
        hub.Clients.Group(StreamHub.GroupName(channelId)).SendAsync("SceneChanged", new
        {
            channelId,
            fromState = fromState.ToString(),
            toState = toState.ToString(),
            trigger = trigger.ToString(),
            occurredAt
        });

    public Task BroadcastMetricsUpdatedAsync(Guid channelId, int bitrateKbps, double packetLossPercent, double fpsIn, DateTimeOffset timestamp) =>
        hub.Clients.Group(StreamHub.GroupName(channelId)).SendAsync("MetricsUpdated", new
        {
            channelId,
            bitrateKbps,
            packetLossPercent,
            fpsIn,
            timestamp
        });

    public Task BroadcastLogAppendedAsync(Guid channelId, string level, string message, DateTimeOffset timestamp) =>
        hub.Clients.Group(StreamHub.GroupName(channelId)).SendAsync("LogAppended", new
        {
            channelId,
            level,
            message,
            timestamp
        });
}
