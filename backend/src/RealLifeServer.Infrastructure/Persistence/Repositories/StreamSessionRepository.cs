using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Persistence.Repositories;

public class StreamSessionRepository(ApplicationDbContext db) : IStreamSessionRepository
{
    public Task<StreamSession?> GetActiveForChannelAsync(Guid channelId, CancellationToken ct = default) =>
        db.StreamSessions
            .Where(s => s.ChannelId == channelId && s.Status == StreamSessionStatus.Running)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

    public Task<StreamSession?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.StreamSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public void Add(StreamSession session) => db.StreamSessions.Add(session);
}
