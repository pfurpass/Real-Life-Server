using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Common.Utils;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Channels.Commands;

public sealed record CreateChannelCommand(Guid OwnerUserId, string Name, IngestProtocol IngestProtocols) : IRequest<ChannelDto>;

public sealed class CreateChannelCommandHandler(IChannelRepository channels, IApplicationDbContext db)
    : IRequestHandler<CreateChannelCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(CreateChannelCommand request, CancellationToken ct)
    {
        var channel = new Channel
        {
            OwnerUserId = request.OwnerUserId,
            Name = request.Name,
            Slug = StreamKeyGenerator.Slugify(request.Name),
            StreamKey = StreamKeyGenerator.Generate(),
            IngestProtocols = request.IngestProtocols == IngestProtocol.None
                ? IngestProtocol.Rtmp | IngestProtocol.Srt
                : request.IngestProtocols,
            CurrentSceneState = SceneState.Offline
        };

        channels.Add(channel);
        await db.SaveChangesAsync(ct);

        return ChannelDto.FromEntity(channel);
    }
}
