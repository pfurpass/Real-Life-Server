using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Common.Interfaces;

public interface IStreamSessionRepository
{
    Task<StreamSession?> GetActiveForChannelAsync(Guid channelId, CancellationToken ct = default);
    Task<StreamSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(StreamSession session);
}
