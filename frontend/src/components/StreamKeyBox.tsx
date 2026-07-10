import { useState } from 'react';

export function StreamKeyBox({
  streamKey,
  rtmpUrl,
  srtUrl,
  onRegenerate
}: {
  streamKey: string;
  rtmpUrl: string;
  srtUrl: string;
  onRegenerate: () => Promise<void>;
}) {
  const [visible, setVisible] = useState(false);
  const [busy, setBusy] = useState(false);

  const masked = '•'.repeat(Math.max(8, streamKey.length));

  const copy = (value: string) => navigator.clipboard.writeText(value);

  const regenerate = async () => {
    if (!confirm('Neuen Stream-Key generieren? Der alte Key wird sofort ungültig und der Encoder verliert die Verbindung.')) {
      return;
    }
    setBusy(true);
    try {
      await onRegenerate();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3 rounded-lg bg-slate-900 p-4">
      <h3 className="text-sm font-semibold text-slate-300">Ingest-Zugangsdaten</h3>

      <div className="space-y-1">
        <label className="text-xs text-slate-500">Stream-Key</label>
        <div className="flex items-center gap-2">
          <code className="flex-1 truncate rounded bg-slate-800 px-2 py-1.5 text-sm">
            {visible ? streamKey : masked}
          </code>
          <button className="rounded bg-slate-800 px-2 py-1.5 text-xs hover:bg-slate-700" onClick={() => setVisible((v) => !v)}>
            {visible ? 'Verbergen' : 'Anzeigen'}
          </button>
          <button className="rounded bg-slate-800 px-2 py-1.5 text-xs hover:bg-slate-700" onClick={() => copy(streamKey)}>
            Kopieren
          </button>
        </div>
      </div>

      <div className="space-y-1">
        <label className="text-xs text-slate-500">RTMP-URL</label>
        <code className="block truncate rounded bg-slate-800 px-2 py-1.5 text-sm">{rtmpUrl}</code>
      </div>

      <div className="space-y-1">
        <label className="text-xs text-slate-500">SRT-URL</label>
        <code className="block truncate rounded bg-slate-800 px-2 py-1.5 text-sm">{srtUrl}</code>
      </div>

      <button
        disabled={busy}
        onClick={regenerate}
        className="w-full rounded bg-red-900/50 px-3 py-1.5 text-sm text-red-300 hover:bg-red-900/80 disabled:opacity-50"
      >
        Stream-Key neu generieren
      </button>
    </div>
  );
}
