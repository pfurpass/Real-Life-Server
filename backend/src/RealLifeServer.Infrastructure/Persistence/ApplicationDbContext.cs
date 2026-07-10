using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<StreamDestination> StreamDestinations => Set<StreamDestination>();
    public DbSet<StreamSession> StreamSessions => Set<StreamSession>();
    public DbSet<SceneEvent> SceneEvents => Set<SceneEvent>();
    public DbSet<StreamMetricSample> StreamMetricSamples => Set<StreamMetricSample>();
    public DbSet<HeartbeatRecord> HeartbeatRecords => Set<HeartbeatRecord>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(builder);
    }
}
