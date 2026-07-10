import type { SceneState } from '../types';

const LABELS: Record<SceneState, string> = {
  Offline: 'Offline',
  Connecting: 'Verbindet…',
  Live: 'Live',
  Degraded: 'Live (beeinträchtigt)',
  Reconnecting: 'Verbindung wird wiederhergestellt',
  Brb: 'Be Right Back'
};

const COLORS: Record<SceneState, string> = {
  Offline: 'bg-scene-offline',
  Connecting: 'bg-scene-connecting',
  Live: 'bg-scene-live',
  Degraded: 'bg-scene-degraded',
  Reconnecting: 'bg-scene-reconnecting',
  Brb: 'bg-scene-brb'
};

export function SceneStatusBadge({ state }: { state: SceneState }) {
  return (
    <span className="inline-flex items-center gap-2 rounded-full bg-slate-800 px-3 py-1 text-sm font-medium">
      <span className={`h-2.5 w-2.5 rounded-full ${COLORS[state]}`} />
      {LABELS[state]}
    </span>
  );
}
