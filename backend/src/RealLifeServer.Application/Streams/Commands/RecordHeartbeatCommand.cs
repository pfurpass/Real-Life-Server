using MediatR;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Streams.Commands;

/// <summary>
/// Optional explicit heartbeat from an encoder-side companion agent, in addition to the
/// implicit heartbeat derived from MediaMTX webhooks. See docs/CONCEPT.md, chapter 13.
/// </summary>
public sealed record RecordHeartbeatCommand(Guid ChannelId, string Source) : IRequest;

public sealed class RecordHeartbeatCommandHandler(
    IChannelRepository channels,
    IApplicationDbContext db,
    ISceneRuntimeStateStore runtimeState,
    IDateTimeProvider clock)
    : IRequestHandler<RecordHeartbeatCommand>
{
    public async Task Handle(RecordHeartbeatCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        var now = clock.UtcNow;
        runtimeState.RecordLastHeartbeat(channel.Id, now);

        db.HeartbeatRecords.Add(new HeartbeatRecord { ChannelId = channel.Id, ReceivedAt = now, Source = request.Source });
        await db.SaveChangesAsync(ct);
    }
}
