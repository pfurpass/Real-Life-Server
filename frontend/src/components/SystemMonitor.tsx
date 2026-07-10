import { useEffect, useState } from 'react';
import { SystemApi } from '../api/endpoints';
import type { HostSystemStats } from '../types';

function Meter({ label, value, unit }: { label: string; value: number; unit: string }) {
  const pct = unit === '%' ? Math.min(100, Math.max(0, value)) : Math.min(100, (value / 1000) * 100);
  return (
    <div>
      <div className="mb-1 flex justify-between text-xs text-slate-400">
        <span>{label}</span>
        <span>
          {value.toFixed(1)} {unit}
        </span>
      </div>
      <div className="h-1.5 w-full rounded-full bg-slate-800">
        <div
          className={`h-1.5 rounded-full ${pct > 85 ? 'bg-red-500' : pct > 65 ? 'bg-amber-500' : 'bg-emerald-500'}`}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}

export function SystemMonitor() {
  const [stats, setStats] = useState<HostSystemStats | null>(null);

  useEffect(() => {
    let cancelled = false;
    const poll = async () => {
      try {
        const result = await SystemApi.stats();
        if (!cancelled) {
          setStats(result);
        }
      } catch {
        // Admin/Moderator only endpoint - silently skip for Streamer accounts.
      }
    };
    poll();
    const interval = setInterval(poll, 5000);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  if (!stats) {
    return null;
  }

  return (
    <div className="space-y-3 rounded-lg bg-slate-900 p-4">
      <h3 className="text-sm font-semibold text-slate-300">Server-Auslastung</h3>
      <Meter label="CPU" value={stats.cpuUsagePercent} unit="%" />
      <Meter label="RAM" value={stats.ramUsagePercent} unit="%" />
      <Meter label="Netzwerk" value={stats.networkUsageMbps} unit="Mbps" />
    </div>
  );
}
