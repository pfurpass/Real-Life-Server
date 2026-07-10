import type { SceneEventDto } from '../types';

const TRIGGER_LABELS: Record<string, string> = {
  EncoderConnected: 'Encoder verbunden',
  FirstKeyframeReceived: 'Erstes Bild empfangen',
  ConnectTimeoutElapsed: 'Verbindungs-Timeout',
  BitrateBelowThreshold: 'Bitrate zu niedrig',
  PacketLossAboveThreshold: 'Paketverlust zu hoch',
  MetricsRecovered: 'Werte erholt',
  EncoderDisconnected: 'Encoder getrennt',
  ReconnectTimeoutElapsed: 'Reconnect-Timeout',
  ManualStop: 'Manuell gestoppt',
  ChannelSuspended: 'Kanal gesperrt',
  ManualOverride: 'Manuelle Szene erzwungen'
};

export function LogPanel({ events }: { events: SceneEventDto[] }) {
  return (
    <div className="rounded-lg bg-slate-900 p-4">
      <h3 className="mb-3 text-sm font-semibold text-slate-300">Ereignisverlauf</h3>
      <ul className="max-h-80 space-y-2 overflow-y-auto text-sm">
        {events.length === 0 && <li className="text-slate-500">Noch keine Ereignisse.</li>}
        {events.map((e) => (
          <li key={e.id} className="flex items-center justify-between gap-3 border-b border-slate-800 pb-2 last:border-0">
            <span className="text-slate-300">
              {e.fromState} → <span className="font-medium text-slate-100">{e.toState}</span>
              <span className="ml-2 text-xs text-slate-500">{TRIGGER_LABELS[e.trigger] ?? e.trigger}</span>
            </span>
            <time className="shrink-0 text-xs text-slate-500">{new Date(e.occurredAt).toLocaleTimeString('de-DE')}</time>
          </li>
        ))}
      </ul>
    </div>
  );
}
