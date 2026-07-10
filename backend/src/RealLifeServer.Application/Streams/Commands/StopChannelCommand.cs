using MediatR;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.Services;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.Commands;

/// <summary>Operator explicitly ends the channel: compositor stops, session closes, scene goes Offline.</summary>
public sealed record StopChannelCommand(Guid ChannelId) : IRequest;

public sealed class StopChannelCommandHandler(SceneTransitionService transitions, IStreamOrchestrator orchestrator)
    : IRequestHandler<StopChannelCommand>
{
    public async Task Handle(StopChannelCommand request, CancellationToken ct)
    {
        await transitions.ApplyTriggerAsync(request.ChannelId, SceneTrigger.ManualStop, ct: ct);
        await orchestrator.StopEncoderAsync(request.ChannelId, ct);
    }
}
