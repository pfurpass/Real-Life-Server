import { useState } from 'react';
import type { SceneState } from '../types';
import { StreamsApi } from '../api/endpoints';

const OVERRIDES: { state: SceneState; label: string }[] = [
  { state: 'Live', label: 'Live erzwingen' },
  { state: 'Brb', label: 'BRB erzwingen' },
  { state: 'Offline', label: 'Offline erzwingen' }
];

export function SceneControlPanel({ channelId, onStop }: { channelId: string; onStop: () => void }) {
  const [busy, setBusy] = useState<SceneState | null>(null);

  const trigger = async (state: SceneState) => {
    setBusy(state);
    try {
      await StreamsApi.forceScene(channelId, state);
    } finally {
      setBusy(null);
    }
  };

  const stop = async () => {
    if (!confirm('Kanal wirklich stoppen? Der Compositor-Prozess wird beendet.')) {
      return;
    }
    await StreamsApi.stop(channelId);
    onStop();
  };

  return (
    <div className="space-y-3 rounded-lg bg-slate-900 p-4">
      <h3 className="text-sm font-semibold text-slate-300">Manuelle Szenensteuerung</h3>
      <div className="flex flex-wrap gap-2">
        {OVERRIDES.map((o) => (
          <button
            key={o.state}
            disabled={busy !== null}
            onClick={() => trigger(o.state)}
            className="rounded bg-slate-800 px-3 py-1.5 text-sm hover:bg-slate-700 disabled:opacity-50"
          >
            {busy === o.state ? '…' : o.label}
          </button>
        ))}
        <button onClick={stop} className="ml-auto rounded bg-red-900/50 px-3 py-1.5 text-sm text-red-300 hover:bg-red-900/80">
          Kanal stoppen
        </button>
      </div>
    </div>
  );
}
