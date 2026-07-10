using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Commands;

public sealed record SetStreamDestinationEnabledCommand(Guid ChannelId, Guid DestinationId, bool IsEnabled) : IRequest<ChannelDto>;

public sealed class SetStreamDestinationEnabledCommandHandler(
    IChannelRepository channels,
    IApplicationDbContext db,
    IStreamOrchestrator orchestrator)
    : IRequestHandler<SetStreamDestinationEnabledCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(SetStreamDestinationEnabledCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        var destination = channel.Destinations.FirstOrDefault(d => d.Id == request.DestinationId)
            ?? throw new NotFoundException(nameof(StreamDestination), request.DestinationId);

        var changed = destination.IsEnabled != request.IsEnabled;
        destination.IsEnabled = request.IsEnabled;
        await db.SaveChangesAsync(ct);

        if (changed)
        {
            await orchestrator.StopEncoderAsync(channel.Id, ct);
        }

        return ChannelDto.FromEntity(channel);
    }
}
