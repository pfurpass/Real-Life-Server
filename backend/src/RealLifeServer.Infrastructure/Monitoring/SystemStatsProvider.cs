using System.Diagnostics;
using RealLifeServer.Application.Common.Interfaces;

namespace RealLifeServer.Infrastructure.Monitoring;

/// <summary>
/// Host-level CPU/RAM/network usage of the media node, shown in the admin dashboard
/// (docs/CONCEPT.md, chapter 9 "GET /api/system/stats"). Reads /proc on Linux; falls back to
/// process-level figures on other platforms so the endpoint never throws in local dev.
/// </summary>
public class SystemStatsProvider : ISystemStatsProvider
{
    private (long Idle, long Total)? _lastCpuSample;
    private (long Bytes, DateTimeOffset At)? _lastNetSample;

    public Task<HostSystemStats> GetCurrentStatsAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(GetFallbackStats());
        }

        var cpu = ReadCpuUsagePercent();
        var ram = ReadRamUsagePercent();
        var net = ReadNetworkUsageMbps();
        return Task.FromResult(new HostSystemStats(cpu, ram, net));
    }

    private double ReadCpuUsagePercent()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").First(l => l.StartsWith("cpu "));
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
            var idle = parts[3] + parts[4]; // idle + iowait
            var total = parts.Sum();

            if (_lastCpuSample is { } last)
            {
                var deltaIdle = idle - last.Idle;
                var deltaTotal = total - last.Total;
                _lastCpuSample = (idle, total);
                return deltaTotal <= 0 ? 0 : 100.0 * (1.0 - (double)deltaIdle / deltaTotal);
            }

            _lastCpuSample = (idle, total);
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static double ReadRamUsagePercent()
    {
        try
        {
            var values = File.ReadLines("/proc/meminfo")
                .Where(l => l.StartsWith("MemTotal:") || l.StartsWith("MemAvailable:"))
                .ToDictionary(
                    l => l.Split(':')[0],
                    l => long.Parse(l.Split(':')[1].Trim().Split(' ')[0]));

            if (!values.TryGetValue("MemTotal", out var total) || total == 0 || !values.TryGetValue("MemAvailable", out var available))
            {
                return 0;
            }

            return 100.0 * (1.0 - (double)available / total);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private double ReadNetworkUsageMbps()
    {
        try
        {
            long totalBytes = 0;
            foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
            {
                var parts = line.Split(':');
                if (parts.Length != 2 || parts[0].Trim() == "lo")
                {
                    continue;
                }

                var fields = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                totalBytes += long.Parse(fields[0]) + long.Parse(fields[8]); // rx bytes + tx bytes
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastNetSample is { } last)
            {
                var elapsed = (now - last.At).TotalSeconds;
                _lastNetSample = (totalBytes, now);
                return elapsed <= 0 ? 0 : (totalBytes - last.Bytes) * 8.0 / 1_000_000 / elapsed;
            }

            _lastNetSample = (totalBytes, now);
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static HostSystemStats GetFallbackStats()
    {
        using var process = Process.GetCurrentProcess();
        var ramMb = process.WorkingSet64 / 1024.0 / 1024.0;
        return new HostSystemStats(0, ramMb, 0);
    }
}
