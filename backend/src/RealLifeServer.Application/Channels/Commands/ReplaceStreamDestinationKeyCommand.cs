using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Commands;

/// <summary>
/// Replaces a destination's stream key wholesale - the existing key is never returned or
/// editable, only ever overwritten by a new one (mirrors RegenerateStreamKeyCommand's approach
/// for the channel's own ingest key).
/// </summary>
public sealed record ReplaceStreamDestinationKeyCommand(Guid ChannelId, Guid DestinationId, string NewStreamKeyPlainText) : IRequest<ChannelDto>;

public sealed class ReplaceStreamDestinationKeyCommandHandler(
    IChannelRepository channels,
    IApplicationDbContext db,
    IEncryptionService encryption,
    IStreamOrchestrator orchestrator)
    : IRequestHandler<ReplaceStreamDestinationKeyCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(ReplaceStreamDestinationKeyCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewStreamKeyPlainText))
        {
            throw new ValidationException("A new stream key is required.");
        }

        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        var destination = channel.Destinations.FirstOrDefault(d => d.Id == request.DestinationId)
            ?? throw new NotFoundException(nameof(StreamDestination), request.DestinationId);

        destination.StreamKeyEncrypted = encryption.Encrypt(request.NewStreamKeyPlainText);
        await db.SaveChangesAsync(ct);

        // A disabled destination's key isn't part of the compositor's resolved output URLs at
        // all - nothing to restart for until it's enabled.
        if (destination.IsEnabled)
        {
            await orchestrator.StopEncoderAsync(channel.Id, ct);
        }

        return ChannelDto.FromEntity(channel);
    }
}
