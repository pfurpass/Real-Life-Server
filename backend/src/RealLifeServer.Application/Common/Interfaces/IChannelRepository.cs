using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Common.Interfaces;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Channel?> GetByStreamKeyAsync(string streamKey, CancellationToken ct = default);
    Task<Channel?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<Channel>> GetForOwnerAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<List<Channel>> GetAllAsync(CancellationToken ct = default);
    void Add(Channel channel);
    void Remove(Channel channel);
}
