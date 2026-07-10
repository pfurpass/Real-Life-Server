using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RealLifeServer.Api.Hubs;

/// <summary>
/// Real-time channel: scene changes, metric updates, log lines (docs/CONCEPT.md, chapter 8).
/// Clients join a per-channel group so broadcasts stay scoped to the dashboards that care.
/// </summary>
[Authorize]
public class StreamHub : Hub
{
    public async Task JoinChannel(Guid channelId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channelId));

    public async Task LeaveChannel(Guid channelId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId));

    public static string GroupName(Guid channelId) => $"channel:{channelId}";
}
