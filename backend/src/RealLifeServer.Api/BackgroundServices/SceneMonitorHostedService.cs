using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.Services;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Api.BackgroundServices;

/// <summary>
/// The heartbeat of the whole system (docs/CONCEPT.md, chapter 5.4 and "Automatisierungen").
/// Every tick, for every channel that is not currently Offline:
///   1. pulls live ingest stats from MediaMTX,
///   2. feeds them into the scene state machine as triggers,
///   3. self-heals a crashed compositor process,
///   4. persists a metric sample and broadcasts it to connected dashboards.
/// </summary>
public class SceneMonitorHostedService(IServiceScopeFactory scopeFactory, ILogger<SceneMonitorHostedService> logger)
    : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SceneMonitorHostedService tick failed");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var statsProvider = scope.ServiceProvider.GetRequiredService<IStreamStatsProvider>();
        var systemStats = scope.ServiceProvider.GetRequiredService<ISystemStatsProvider>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IStreamOrchestrator>();
        var sessions = scope.ServiceProvider.GetRequiredService<IStreamSessionRepository>();
        var runtimeState = scope.ServiceProvider.GetRequiredService<ISceneRuntimeStateStore>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<ISceneEventBroadcaster>();
        var transitions = scope.ServiceProvider.GetRequiredService<SceneTransitionService>();

        var channels = await db.Channels
            .Include(c => c.Destinations)
            .Where(c => c.IsActive && c.CurrentSceneState != SceneState.Offline)
            .ToListAsync(ct);

        var host = await systemStats.GetCurrentStatsAsync(ct);

        foreach (var channel in channels)
        {
            var stats = await statsProvider.GetChannelStatsAsync(channel.StreamKey, ct);

            // Self-healing: docs/CONCEPT.md chapter 12 - a crashed compositor is restarted here
            // rather than in the orchestrator's crash handler, keeping "when to retry" a
            // monitoring concern instead of a process-management one.
            if (!orchestrator.IsEncoderRunning(channel.Id))
            {
                await orchestrator.EnsureEncoderRunningAsync(channel, channel.Destinations.ToList(), ct);
                await orchestrator.ApplySceneAsync(channel.Id, channel.CurrentSceneState, ct: ct);
            }

            if (stats.IsPublishing)
            {
                runtimeState.RecordLastHeartbeat(channel.Id, DateTimeOffset.UtcNow);
            }

            await EvaluateTriggersAsync(channel, stats, runtimeState, transitions, ct);

            var activeSession = await sessions.GetActiveForChannelAsync(channel.Id, ct);
            if (activeSession is not null)
            {
                db.StreamMetricSamples.Add(new StreamMetricSample
                {
                    StreamSessionId = activeSession.Id,
                    BitrateKbps = stats.BitrateKbps,
                    PacketLossPercent = stats.PacketLossPercent,
                    FpsIn = stats.FpsIn,
                    CpuUsagePercent = host.CpuUsagePercent,
                    RamUsagePercent = host.RamUsagePercent,
                    NetworkUsageMbps = host.NetworkUsageMbps
                });

                await broadcaster.BroadcastMetricsUpdatedAsync(channel.Id, stats.BitrateKbps, stats.PacketLossPercent, stats.FpsIn, DateTimeOffset.UtcNow);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EvaluateTriggersAsync(
        Channel channel,
        ChannelIngestStats stats,
        ISceneRuntimeStateStore runtimeState,
        SceneTransitionService transitions,
        CancellationToken ct)
    {
        var thresholds = channel.SceneThresholds;
        var lastHeartbeat = runtimeState.GetLastHeartbeat(channel.Id);
        var heartbeatStale = lastHeartbeat is null ||
            DateTimeOffset.UtcNow - lastHeartbeat.Value > TimeSpan.FromSeconds(thresholds.HeartbeatTimeoutSeconds);

        switch (channel.CurrentSceneState)
        {
            case SceneState.Connecting:
                // Deliberately not gated on heartbeat staleness here: a fresh connection has no
                // heartbeat yet by definition (MediaMTX only reports a path "ready" once the
                // first frame arrives, which is exactly the event this state is waiting for).
                // ConnectTimeoutSeconds alone bounds how long we wait before giving up.
                await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.ConnectTimeoutElapsed, ct: ct);
                break;

            case SceneState.Live or SceneState.Degraded:
                if (!stats.IsPublishing || heartbeatStale)
                {
                    await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.EncoderDisconnected, ct: ct);
                    break;
                }

                if (stats.BitrateKbps < thresholds.MinBitrateKbps)
                {
                    await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.BitrateBelowThreshold,
                        new Dictionary<string, object> { ["bitrateKbps"] = stats.BitrateKbps }, ct);
                }
                else if (stats.PacketLossPercent > thresholds.MaxPacketLossPercent)
                {
                    await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.PacketLossAboveThreshold,
                        new Dictionary<string, object> { ["packetLossPercent"] = stats.PacketLossPercent }, ct);
                }
                else if (channel.CurrentSceneState == SceneState.Degraded)
                {
                    await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.MetricsRecovered, ct: ct);
                }
                break;

            case SceneState.Reconnecting:
                if (stats.IsPublishing && !heartbeatStale)
                {
                    await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.EncoderConnected, ct: ct);
                }
                else
                {
                    await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.ReconnectTimeoutElapsed, ct: ct);
                }
                break;

            case SceneState.Brb:
                if (stats.IsPublishing && !heartbeatStale)
                {
                    await transitions.ApplyTriggerAsync(channel.Id, SceneTrigger.EncoderConnected, ct: ct);
                }
                break;
        }
    }
}
