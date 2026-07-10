using MediatR;
using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.Dtos;

namespace RealLifeServer.Application.Streams.Queries;

public sealed record GetSceneEventsQuery(Guid ChannelId, int Take = 100) : IRequest<IReadOnlyList<SceneEventDto>>;

public sealed class GetSceneEventsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetSceneEventsQuery, IReadOnlyList<SceneEventDto>>
{
    public async Task<IReadOnlyList<SceneEventDto>> Handle(GetSceneEventsQuery request, CancellationToken ct)
    {
        return await db.SceneEvents
            .Where(e => e.ChannelId == request.ChannelId)
            .OrderByDescending(e => e.OccurredAt)
            .Take(request.Take)
            .Select(e => new SceneEventDto(e.Id, e.FromState, e.ToState, e.Trigger, e.OccurredAt, e.MetadataJson))
            .ToListAsync(ct);
    }
}
