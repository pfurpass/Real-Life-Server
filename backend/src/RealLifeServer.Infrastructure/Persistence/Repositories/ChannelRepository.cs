using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Repositories;

public class ChannelRepository(ApplicationDbContext db) : IChannelRepository
{
    private IQueryable<Channel> WithDestinations() => db.Channels.Include(c => c.Destinations);

    public Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        WithDestinations().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Channel?> GetByStreamKeyAsync(string streamKey, CancellationToken ct = default) =>
        WithDestinations().FirstOrDefaultAsync(c => c.StreamKey == streamKey, ct);

    public Task<Channel?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        WithDestinations().FirstOrDefaultAsync(c => c.Slug == slug, ct);

    public Task<List<Channel>> GetForOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
        WithDestinations().Where(c => c.OwnerUserId == ownerUserId).OrderBy(c => c.Name).ToListAsync(ct);

    public Task<List<Channel>> GetAllAsync(CancellationToken ct = default) =>
        WithDestinations().OrderBy(c => c.Name).ToListAsync(ct);

    public void Add(Channel channel) => db.Channels.Add(channel);

    public void Remove(Channel channel) => db.Channels.Remove(channel);
}
