using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Commands;

/// <summary>Reassigns DisplayOrder from the given ordering, which must cover exactly the channel's current destinations.</summary>
public sealed record ReorderStreamDestinationsCommand(Guid ChannelId, IReadOnlyList<Guid> OrderedDestinationIds) : IRequest<ChannelDto>;

public sealed class ReorderStreamDestinationsCommandHandler(
    IChannelRepository channels,
    IApplicationDbContext db,
    IStreamOrchestrator orchestrator)
    : IRequestHandler<ReorderStreamDestinationsCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(ReorderStreamDestinationsCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        var existingIds = channel.Destinations.Select(d => d.Id).ToHashSet();
        if (request.OrderedDestinationIds.Count != existingIds.Count || !existingIds.SetEquals(request.OrderedDestinationIds))
        {
            throw new ValidationException("The provided order must contain exactly the channel's current destinations, each exactly once.");
        }

        for (var i = 0; i < request.OrderedDestinationIds.Count; i++)
        {
            var destination = channel.Destinations.First(d => d.Id == request.OrderedDestinationIds[i]);
            destination.DisplayOrder = (short)i;
        }

        await db.SaveChangesAsync(ct);

        // Order only reaches the FFmpeg command line via the tee muxer's target list
        // (FfmpegCommandBuilder.BuildOutputArgs), which only exists once 2+ destinations are
        // enabled at the same time - nothing to restart for otherwise.
        if (channel.Destinations.Count(d => d.IsEnabled) >= 2)
        {
            await orchestrator.StopEncoderAsync(channel.Id, ct);
        }

        return ChannelDto.FromEntity(channel);
    }
}
