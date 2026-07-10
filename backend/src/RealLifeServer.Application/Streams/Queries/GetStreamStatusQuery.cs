using MediatR;
using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.Dtos;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.Queries;

public sealed record GetStreamStatusQuery(Guid ChannelId) : IRequest<StreamStatusDto>;

public sealed class GetStreamStatusQueryHandler(
    IChannelRepository channels,
    IStreamSessionRepository sessions,
    IApplicationDbContext db,
    ISceneRuntimeStateStore runtimeState)
    : IRequestHandler<GetStreamStatusQuery, StreamStatusDto>
{
    public async Task<StreamStatusDto> Handle(GetStreamStatusQuery request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        var activeSession = await sessions.GetActiveForChannelAsync(channel.Id, ct);
        var lastSample = activeSession is null
            ? null
            : await db.StreamMetricSamples
                .Where(s => s.StreamSessionId == activeSession.Id)
                .OrderByDescending(s => s.RecordedAt)
                .FirstOrDefaultAsync(ct);

        return new StreamStatusDto(
            channel.Id,
            channel.CurrentSceneState,
            lastSample?.BitrateKbps,
            lastSample?.PacketLossPercent,
            runtimeState.GetLastHeartbeat(channel.Id),
            channel.CurrentSceneState is SceneState.Live or SceneState.Degraded or SceneState.Connecting);
    }
}
