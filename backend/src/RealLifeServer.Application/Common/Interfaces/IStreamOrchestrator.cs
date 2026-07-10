using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>
/// Coordinates one <see cref="ISceneEncoder"/> per active channel on this media node.
/// Consumed by the webhook handlers and <c>SceneMonitorHostedService</c>.
/// </summary>
public interface IStreamOrchestrator
{
    Task EnsureEncoderRunningAsync(Channel channel, IReadOnlyList<StreamDestination> destinations, CancellationToken ct = default);

    Task ApplySceneAsync(Guid channelId, SceneState state, TimeSpan? countdown = null, CancellationToken ct = default);

    Task StopEncoderAsync(Guid channelId, CancellationToken ct = default);

    bool IsEncoderRunning(Guid channelId);
}
