import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import type { MetricSampleDto } from '../types';

export function BitrateChart({ samples }: { samples: MetricSampleDto[] }) {
  const data = samples.map((s) => ({
    time: new Date(s.recordedAt).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
    bitrate: s.bitrateKbps,
    packetLoss: s.packetLossPercent
  }));

  return (
    <div className="h-64 w-full rounded-lg bg-slate-900 p-4">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data}>
          <defs>
            <linearGradient id="bitrateFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#3b82f6" stopOpacity={0.4} />
              <stop offset="95%" stopColor="#3b82f6" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" />
          <XAxis dataKey="time" stroke="#64748b" fontSize={11} minTickGap={40} />
          <YAxis stroke="#64748b" fontSize={11} unit=" kbps" />
          <Tooltip
            contentStyle={{ backgroundColor: '#0f172a', border: '1px solid #1e293b', fontSize: 12 }}
            labelStyle={{ color: '#94a3b8' }}
          />
          <Area type="monotone" dataKey="bitrate" name="Bitrate" stroke="#3b82f6" fill="url(#bitrateFill)" strokeWidth={2} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
