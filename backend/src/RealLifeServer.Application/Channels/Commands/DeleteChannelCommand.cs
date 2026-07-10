using MediatR;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Commands;

public sealed record DeleteChannelCommand(Guid ChannelId) : IRequest;

public sealed class DeleteChannelCommandHandler(
    IChannelRepository channels,
    IApplicationDbContext db,
    IStreamOrchestrator orchestrator)
    : IRequestHandler<DeleteChannelCommand>
{
    public async Task Handle(DeleteChannelCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        await orchestrator.StopEncoderAsync(channel.Id, ct);

        channels.Remove(channel);
        await db.SaveChangesAsync(ct);
    }
}
