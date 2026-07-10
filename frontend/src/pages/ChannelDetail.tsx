import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ChannelsApi, StreamsApi } from '../api/endpoints';
import { buildRtmpUrl, buildSrtUrl } from '../config/serverConfig';
import { LivePreviewPlayer } from '../components/LivePreviewPlayer';
import { SceneStatusBadge } from '../components/SceneStatusBadge';
import { BitrateChart } from '../components/BitrateChart';
import { StreamKeyBox } from '../components/StreamKeyBox';
import { LogPanel } from '../components/LogPanel';
import { SceneControlPanel } from '../components/SceneControlPanel';
import { useStreamHub } from '../hooks/useStreamHub';
import type { ChannelDto, MetricSampleDto, SceneEventDto } from '../types';

export function ChannelDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [channel, setChannel] = useState<ChannelDto | null>(null);
  const [metrics, setMetrics] = useState<MetricSampleDto[]>([]);
  const [events, setEvents] = useState<SceneEventDto[]>([]);

  const { lastSceneChange, lastMetrics } = useStreamHub(id ?? null);

  const reload = useCallback(async () => {
    if (!id) return;
    const [c, m, e] = await Promise.all([ChannelsApi.get(id), StreamsApi.metrics(id, 60), StreamsApi.events(id, 50)]);
    setChannel(c);
    setMetrics(m);
    setEvents(e);
  }, [id]);

  useEffect(() => {
    reload();
  }, [reload]);

  // Keep the cached scene state in sync with real-time pushes instead of waiting for the next poll.
  useEffect(() => {
    if (lastSceneChange && channel && lastSceneChange.channelId === channel.id) {
      setChannel((prev) => (prev ? { ...prev, currentSceneState: lastSceneChange.toState } : prev));
      reload();
    }
  }, [lastSceneChange, channel, reload]);

  useEffect(() => {
    if (lastMetrics && id && lastMetrics.channelId === id) {
      setMetrics((prev) => [
        ...prev,
        {
          recordedAt: lastMetrics.timestamp,
          bitrateKbps: lastMetrics.bitrateKbps,
          packetLossPercent: lastMetrics.packetLossPercent,
          fpsIn: lastMetrics.fpsIn,
          cpuUsagePercent: 0,
          ramUsagePercent: 0,
          networkUsageMbps: 0
        }
      ].slice(-200));
    }
  }, [lastMetrics, id]);

  if (!channel || !id) {
    return <p className="text-slate-500">Lädt…</p>;
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">{channel.name}</h2>
          <p className="text-xs text-slate-500">/{channel.slug}</p>
        </div>
        <SceneStatusBadge state={channel.currentSceneState} />
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <LivePreviewPlayer streamKey={channel.streamKey} />
          <BitrateChart samples={metrics} />
          <SceneControlPanel channelId={id} onStop={reload} />
        </div>

        <div className="space-y-4">
          <StreamKeyBox
            streamKey={channel.streamKey}
            rtmpUrl={buildRtmpUrl(channel.streamKey)}
            srtUrl={buildSrtUrl(channel.streamKey)}
            onRegenerate={async () => {
              const updated = await ChannelsApi.regenerateKey(id);
              setChannel(updated);
            }}
          />
          <LogPanel events={events} />
        </div>
      </div>

      <button onClick={() => navigate('/')} className="text-sm text-slate-500 hover:text-slate-300">
        ← Zurück zur Übersicht
      </button>
    </div>
  );
}
