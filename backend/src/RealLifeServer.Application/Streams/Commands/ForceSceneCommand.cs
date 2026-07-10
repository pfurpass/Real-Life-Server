using MediatR;
using RealLifeServer.Application.Streams.Services;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.Commands;

/// <summary>
/// Manual operator override, e.g. forcing BRB during a bathroom break even though the
/// encoder is healthy. Automatic detection resumes normally on the next trigger.
/// </summary>
public sealed record ForceSceneCommand(Guid ChannelId, SceneState TargetState) : IRequest;

public sealed class ForceSceneCommandHandler(SceneTransitionService transitions) : IRequestHandler<ForceSceneCommand>
{
    public async Task Handle(ForceSceneCommand request, CancellationToken ct)
    {
        await transitions.ApplyManualOverrideAsync(request.ChannelId, request.TargetState, ct);
    }
}
