using Microsoft.EntityFrameworkCore;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the EF Core DbContext so Application-layer use cases can compose
/// queries without depending on Infrastructure directly.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Channel> Channels { get; }
    DbSet<StreamDestination> StreamDestinations { get; }
    DbSet<StreamSession> StreamSessions { get; }
    DbSet<SceneEvent> SceneEvents { get; }
    DbSet<StreamMetricSample> StreamMetricSamples { get; }
    DbSet<HeartbeatRecord> HeartbeatRecords { get; }
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
