using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Common.Utils;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Commands;

/// <summary>
/// Rotates a channel's ingest key. Also force-stops the current encoder session: an old key
/// must not silently keep publishing after rotation.
/// </summary>
public sealed record RegenerateStreamKeyCommand(Guid ChannelId) : IRequest<ChannelDto>;

public sealed class RegenerateStreamKeyCommandHandler(
    IChannelRepository channels,
    IApplicationDbContext db,
    IStreamOrchestrator orchestrator)
    : IRequestHandler<RegenerateStreamKeyCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(RegenerateStreamKeyCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        channel.StreamKey = StreamKeyGenerator.Generate();
        await db.SaveChangesAsync(ct);

        await orchestrator.StopEncoderAsync(channel.Id, ct);

        return ChannelDto.FromEntity(channel);
    }
}
