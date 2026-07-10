using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Common.Utils;
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
    IEncryptionService encryption,
    IStreamOrchestrator orchestrator)
    : IRequestHandler<AddStreamDestinationCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(AddStreamDestinationCommand request, CancellationToken ct)
    {
        if (!RtmpUrlValidator.IsValid(request.RtmpUrl))
        {
            throw new ValidationException($"\"{request.RtmpUrl}\" is not a valid rtmp:// or rtmps:// URL.");
        }
        if (string.IsNullOrWhiteSpace(request.StreamKeyPlainText))
        {
            throw new ValidationException("A stream key is required.");
        }

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

        // New destinations start enabled (see StreamDestination.IsEnabled's default), so they
        // always change the compositor's active output set - restart so the running FFmpeg
        // process (its tee target list is fixed at process start) picks it up. Controlled: the
        // next SceneMonitorHostedService tick restarts it, same as RegenerateStreamKeyCommand.
        await orchestrator.StopEncoderAsync(channel.Id, ct);

        return ChannelDto.FromEntity(channel);
    }
}
