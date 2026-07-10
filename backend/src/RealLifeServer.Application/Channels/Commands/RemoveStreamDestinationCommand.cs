using MediatR;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Commands;

public sealed record RemoveStreamDestinationCommand(Guid ChannelId, Guid DestinationId) : IRequest;

public sealed class RemoveStreamDestinationCommandHandler(IChannelRepository channels, IApplicationDbContext db)
    : IRequestHandler<RemoveStreamDestinationCommand>
{
    public async Task Handle(RemoveStreamDestinationCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        var destination = channel.Destinations.FirstOrDefault(d => d.Id == request.DestinationId)
            ?? throw new NotFoundException(nameof(StreamDestination), request.DestinationId);

        channel.Destinations.Remove(destination);
        await db.SaveChangesAsync(ct);
    }
}
