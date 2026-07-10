namespace RealLifeServer.Application.Common.Interfaces;

public sealed record HostSystemStats(double CpuUsagePercent, double RamUsagePercent, double NetworkUsageMbps);

/// <summary>Host-level resource usage of the media node, shown in the admin dashboard.</summary>
public interface ISystemStatsProvider
{
    Task<HostSystemStats> GetCurrentStatsAsync(CancellationToken ct = default);
}
