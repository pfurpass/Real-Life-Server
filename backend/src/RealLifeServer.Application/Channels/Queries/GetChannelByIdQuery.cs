using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Queries;

public sealed record GetChannelByIdQuery(Guid ChannelId) : IRequest<ChannelDto>;

public sealed class GetChannelByIdQueryHandler(IChannelRepository channels)
    : IRequestHandler<GetChannelByIdQuery, ChannelDto>
{
    public async Task<ChannelDto> Handle(GetChannelByIdQuery request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        return ChannelDto.FromEntity(channel);
    }
}
