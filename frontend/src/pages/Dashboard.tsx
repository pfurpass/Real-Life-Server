import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { ChannelsApi } from '../api/endpoints';
import { SceneStatusBadge } from '../components/SceneStatusBadge';
import { SystemMonitor } from '../components/SystemMonitor';
import { IngestProtocol, type ChannelDto } from '../types';

export function Dashboard() {
  const [channels, setChannels] = useState<ChannelDto[]>([]);
  const [newChannelName, setNewChannelName] = useState('');
  const [loading, setLoading] = useState(true);

  const reload = () => ChannelsApi.list().then(setChannels);

  useEffect(() => {
    reload().finally(() => setLoading(false));
    const interval = setInterval(reload, 5000);
    return () => clearInterval(interval);
  }, []);

  const createChannel = async (e: FormEvent) => {
    e.preventDefault();
    if (!newChannelName.trim()) {
      return;
    }
    await ChannelsApi.create(newChannelName.trim(), IngestProtocol.Rtmp | IngestProtocol.Srt);
    setNewChannelName('');
    await reload();
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-semibold">Kanäle</h2>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          {loading && <p className="text-slate-500">Lädt…</p>}
          {!loading && channels.length === 0 && (
            <p className="text-slate-500">Noch keine Kanäle. Lege rechts einen neuen an.</p>
          )}

          <ul className="space-y-2">
            {channels.map((c) => (
              <li key={c.id}>
                <Link
                  to={`/channels/${c.id}`}
                  className="flex items-center justify-between rounded-lg bg-slate-900 p-4 hover:bg-slate-800"
                >
                  <div>
                    <p className="font-medium">{c.name}</p>
                    <p className="text-xs text-slate-500">/{c.slug}</p>
                  </div>
                  <SceneStatusBadge state={c.currentSceneState} />
                </Link>
              </li>
            ))}
          </ul>
        </div>

        <div className="space-y-4">
          <form onSubmit={createChannel} className="space-y-3 rounded-lg bg-slate-900 p-4">
            <h3 className="text-sm font-semibold text-slate-300">Neuen Kanal anlegen</h3>
            <input
              className="w-full rounded bg-slate-800 px-3 py-2 text-sm outline-none focus:ring-1 focus:ring-blue-500"
              placeholder="Kanalname"
              value={newChannelName}
              onChange={(e) => setNewChannelName(e.target.value)}
            />
            <button type="submit" className="w-full rounded bg-blue-600 px-3 py-2 text-sm font-medium hover:bg-blue-500">
              Anlegen
            </button>
          </form>

          <SystemMonitor />
        </div>
      </div>
    </div>
  );
}
