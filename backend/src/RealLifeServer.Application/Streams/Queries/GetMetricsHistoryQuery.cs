using MediatR;
using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.Dtos;

namespace RealLifeServer.Application.Streams.Queries;

public sealed record GetMetricsHistoryQuery(Guid ChannelId, TimeSpan Range) : IRequest<IReadOnlyList<MetricSampleDto>>;

public sealed class GetMetricsHistoryQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    : IRequestHandler<GetMetricsHistoryQuery, IReadOnlyList<MetricSampleDto>>
{
    public async Task<IReadOnlyList<MetricSampleDto>> Handle(GetMetricsHistoryQuery request, CancellationToken ct)
    {
        var since = clock.UtcNow - request.Range;

        return await db.StreamMetricSamples
            .Where(s => s.StreamSession!.ChannelId == request.ChannelId && s.RecordedAt >= since)
            .OrderBy(s => s.RecordedAt)
            .Select(s => new MetricSampleDto(
                s.RecordedAt, s.BitrateKbps, s.PacketLossPercent, s.FpsIn,
                s.CpuUsagePercent, s.RamUsagePercent, s.NetworkUsageMbps))
            .ToListAsync(ct);
    }
}
