using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Common.Interfaces;

namespace RealLifeServer.Api.BackgroundServices;

/// <summary>
/// Keeps stream_metric_samples from growing unbounded (docs/CONCEPT.md, chapter 12): raw
/// per-2s samples older than <see cref="RetentionPeriod"/> are deleted. A production deployment
/// would roll them into an hourly-aggregate table first instead of discarding them outright -
/// left as a documented extension point rather than implemented here to keep this service small.
/// </summary>
public class MetricsRetentionHostedService(IServiceScopeFactory scopeFactory, ILogger<MetricsRetentionHostedService> logger)
    : BackgroundService
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RunInterval);
        do
        {
            try
            {
                await PurgeOldSamplesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MetricsRetentionHostedService run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeOldSamplesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var cutoff = DateTimeOffset.UtcNow - RetentionPeriod;
        var deleted = await db.StreamMetricSamples
            .Where(s => s.RecordedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            logger.LogInformation("Purged {Count} stream metric samples older than {Cutoff}", deleted, cutoff);
        }
    }
}
