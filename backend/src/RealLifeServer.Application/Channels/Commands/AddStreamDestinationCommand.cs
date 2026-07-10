using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Channels.Commands;

public sealed record AddStreamDestinationCommand(
    Guid ChannelId,
    StreamPlatform Platform,
    string RtmpUrl,
    string StreamKeyPlainText) : IRequest<ChannelDto>;

public sealed class AddStreamDestinationCommandHandler(
    IChannelRepository channels,
    IApplicationDbContext db,
    IEncryptionService encryption)
    : IRequestHandler<AddStreamDestinationCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(AddStreamDestinationCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        channel.Destinations.Add(new StreamDestination
        {
            ChannelId = channel.Id,
            Platform = request.Platform,
            RtmpUrl = request.RtmpUrl,
            StreamKeyEncrypted = encryption.Encrypt(request.StreamKeyPlainText),
            DisplayOrder = (short)channel.Destinations.Count
        });

        await db.SaveChangesAsync(ct);
        return ChannelDto.FromEntity(channel);
    }
}
