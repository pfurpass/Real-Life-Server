using MediatR;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.Services;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.Commands;

/// <summary>MediaMTX <c>runOnPublish</c>: a client attempts to start publishing. Returns whether the stream key is valid and the channel accepts ingest.</summary>
public sealed record HandlePublishWebhookCommand(string StreamKey) : IRequest<bool>;

public sealed class HandlePublishWebhookCommandHandler(
    IChannelRepository channels,
    IStreamOrchestrator orchestrator,
    SceneTransitionService transitions)
    : IRequestHandler<HandlePublishWebhookCommand, bool>
{
    public async Task<bool> Handle(HandlePublishWebhookCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByStreamKeyAsync(request.StreamKey, ct);
        if (channel is null || !channel.IsActive)
        {
            return false;
        }

        await orchestrator.EnsureEncoderRunningAsync(channel, channel.Destinations.Where(d => d.IsEnabled).ToList(), ct);
        await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.EncoderConnected, ct: ct);
        return true;
    }
}

/// <summary>MediaMTX <c>runOnReady</c>: the first readable frame arrived.</summary>
public sealed record HandleReadyWebhookCommand(string StreamKey) : IRequest;

public sealed class HandleReadyWebhookCommandHandler(IChannelRepository channels, SceneTransitionService transitions)
    : IRequestHandler<HandleReadyWebhookCommand>
{
    public async Task Handle(HandleReadyWebhookCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByStreamKeyAsync(request.StreamKey, ct);
        if (channel is null)
        {
            return;
        }

        await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.FirstKeyframeReceived, ct: ct);
    }
}

/// <summary>MediaMTX <c>runOnNotReady</c> / <c>runOnUnpublish</c>: the encoder connection was lost.</summary>
public sealed record HandleNotReadyWebhookCommand(string StreamKey) : IRequest;

public sealed class HandleNotReadyWebhookCommandHandler(IChannelRepository channels, SceneTransitionService transitions)
    : IRequestHandler<HandleNotReadyWebhookCommand>
{
    public async Task Handle(HandleNotReadyWebhookCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByStreamKeyAsync(request.StreamKey, ct);
        if (channel is null)
        {
            return;
        }

        await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.EncoderDisconnected, ct: ct);
    }
}
