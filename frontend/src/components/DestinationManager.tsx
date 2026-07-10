import { useState, type FormEvent } from 'react';
import { ChannelsApi } from '../api/endpoints';
import { extractErrorMessage } from '../api/client';
import type { StreamDestinationDto, StreamPlatform } from '../types';

const PLATFORM_LABELS: Record<StreamPlatform, string> = {
  Twitch: 'Twitch',
  YouTube: 'YouTube',
  Custom: 'Benutzerdefiniert'
};

const PLATFORM_DEFAULT_RTMP_URL: Record<StreamPlatform, string> = {
  Twitch: 'rtmp://live.twitch.tv/app',
  YouTube: 'rtmp://a.rtmp.youtube.com/live2',
  Custom: ''
};

function looksLikeRtmpUrl(url: string): boolean {
  return /^rtmps?:\/\/.+/i.test(url.trim());
}

/**
 * Destination management for a channel's outbound restream targets (docs/CONCEPT.md, chapter
 * 4/10). The backend never returns a destination's stream key at all (StreamDestinationDto has
 * no key field) - "never shown again" is enforced structurally, not just visually, so this
 * component only ever shows a fixed mask and offers a wholesale replace, never a reveal.
 */
export function DestinationManager({
  channelId,
  destinations,
  onReload
}: {
  channelId: string;
  destinations: StreamDestinationDto[];
  onReload: () => Promise<void>;
}) {
  const [platform, setPlatform] = useState<StreamPlatform>('Twitch');
  const [rtmpUrl, setRtmpUrl] = useState(PLATFORM_DEFAULT_RTMP_URL.Twitch);
  const [streamKey, setStreamKey] = useState('');
  const [adding, setAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  const [busyDestinationId, setBusyDestinationId] = useState<string | null>(null);
  const [rowError, setRowError] = useState<{ id: string; message: string } | null>(null);
  const [replacingKeyFor, setReplacingKeyFor] = useState<string | null>(null);
  const [newKeyValue, setNewKeyValue] = useState('');

  const changePlatform = (next: StreamPlatform) => {
    setPlatform(next);
    // Only swap in the new platform's preset if the field still holds some other platform's
    // preset (or is empty) - don't clobber a URL the user already typed themselves.
    if (Object.values(PLATFORM_DEFAULT_RTMP_URL).includes(rtmpUrl)) {
      setRtmpUrl(PLATFORM_DEFAULT_RTMP_URL[next]);
    }
  };

  const submitAdd = async (e: FormEvent) => {
    e.preventDefault();
    if (!looksLikeRtmpUrl(rtmpUrl)) {
      setAddError('Bitte eine gültige rtmp:// oder rtmps:// URL angeben.');
      return;
    }
    if (!streamKey.trim()) {
      setAddError('Bitte einen Stream-Key angeben.');
      return;
    }
    setAdding(true);
    setAddError(null);
    try {
      await ChannelsApi.addDestination(channelId, platform, rtmpUrl.trim(), streamKey.trim());
      setStreamKey('');
      await onReload();
    } catch (err) {
      setAddError(extractErrorMessage(err, 'Destination konnte nicht hinzugefügt werden.'));
    } finally {
      setAdding(false);
    }
  };

  const toggleEnabled = async (destination: StreamDestinationDto) => {
    setBusyDestinationId(destination.id);
    setRowError(null);
    try {
      await ChannelsApi.setDestinationEnabled(channelId, destination.id, !destination.isEnabled);
      await onReload();
    } catch (err) {
      setRowError({ id: destination.id, message: extractErrorMessage(err, 'Status konnte nicht geändert werden.') });
    } finally {
      setBusyDestinationId(null);
    }
  };

  const remove = async (destination: StreamDestinationDto) => {
    if (!confirm(`"${PLATFORM_LABELS[destination.platform]}"-Ziel wirklich löschen?`)) {
      return;
    }
    setBusyDestinationId(destination.id);
    setRowError(null);
    try {
      await ChannelsApi.removeDestination(channelId, destination.id);
      await onReload();
    } catch (err) {
      setRowError({ id: destination.id, message: extractErrorMessage(err, 'Destination konnte nicht gelöscht werden.') });
    } finally {
      setBusyDestinationId(null);
    }
  };

  const ordered = [...destinations].sort((a, b) => a.displayOrder - b.displayOrder);

  const move = async (index: number, direction: -1 | 1) => {
    const target = index + direction;
    if (target < 0 || target >= ordered.length) {
      return;
    }
    const reordered = [...ordered];
    [reordered[index], reordered[target]] = [reordered[target], reordered[index]];
    setBusyDestinationId(ordered[index].id);
    setRowError(null);
    try {
      await ChannelsApi.reorderDestinations(
        channelId,
        reordered.map((d) => d.id)
      );
      await onReload();
    } catch (err) {
      setRowError({ id: ordered[index].id, message: extractErrorMessage(err, 'Reihenfolge konnte nicht geändert werden.') });
    } finally {
      setBusyDestinationId(null);
    }
  };

  const submitNewKey = async (destination: StreamDestinationDto) => {
    if (!newKeyValue.trim()) {
      setRowError({ id: destination.id, message: 'Bitte einen neuen Stream-Key angeben.' });
      return;
    }
    setBusyDestinationId(destination.id);
    setRowError(null);
    try {
      await ChannelsApi.replaceDestinationKey(channelId, destination.id, newKeyValue.trim());
      setNewKeyValue('');
      setReplacingKeyFor(null);
      await onReload();
    } catch (err) {
      setRowError({ id: destination.id, message: extractErrorMessage(err, 'Stream-Key konnte nicht ersetzt werden.') });
    } finally {
      setBusyDestinationId(null);
    }
  };

  return (
    <div className="space-y-3 rounded-lg bg-slate-900 p-4">
      <h3 className="text-sm font-semibold text-slate-300">Restream-Ziele</h3>

      {ordered.length === 0 && <p className="text-xs text-slate-500">Noch keine Ziele konfiguriert.</p>}

      <ul className="space-y-2" data-testid="destination-list">
        {ordered.map((destination, index) => (
          <li key={destination.id} data-testid={`destination-row-${destination.id}`} className="space-y-2 rounded bg-slate-800 p-3">
            <div className="flex items-center gap-2">
              <span className="rounded bg-slate-700 px-2 py-0.5 text-xs font-medium">{PLATFORM_LABELS[destination.platform]}</span>
              <span className="flex-1 truncate text-sm text-slate-300">{destination.rtmpUrl}</span>
              <span
                data-testid={`destination-status-${destination.id}`}
                className={`text-xs ${destination.isEnabled ? 'text-emerald-400' : 'text-slate-500'}`}
              >
                {destination.isEnabled ? 'aktiv' : 'deaktiviert'}
              </span>
            </div>

            <div className="flex items-center gap-2 text-xs text-slate-500">
              <span>Stream-Key:</span>
              <code data-testid={`destination-key-${destination.id}`}>••••••••</code>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <button
                disabled={busyDestinationId === destination.id}
                onClick={() => toggleEnabled(destination)}
                className="rounded bg-slate-700 px-2 py-1 text-xs hover:bg-slate-600 disabled:opacity-50"
              >
                {destination.isEnabled ? 'Deaktivieren' : 'Aktivieren'}
              </button>
              <button
                disabled={busyDestinationId === destination.id}
                onClick={() => {
                  setReplacingKeyFor(replacingKeyFor === destination.id ? null : destination.id);
                  setNewKeyValue('');
                }}
                className="rounded bg-slate-700 px-2 py-1 text-xs hover:bg-slate-600 disabled:opacity-50"
              >
                Key ersetzen
              </button>
              <button
                disabled={busyDestinationId === destination.id || index === 0}
                onClick={() => move(index, -1)}
                aria-label="Nach oben verschieben"
                className="rounded bg-slate-700 px-2 py-1 text-xs hover:bg-slate-600 disabled:opacity-50"
              >
                ↑
              </button>
              <button
                disabled={busyDestinationId === destination.id || index === ordered.length - 1}
                onClick={() => move(index, 1)}
                aria-label="Nach unten verschieben"
                className="rounded bg-slate-700 px-2 py-1 text-xs hover:bg-slate-600 disabled:opacity-50"
              >
                ↓
              </button>
              <button
                disabled={busyDestinationId === destination.id}
                onClick={() => remove(destination)}
                className="ml-auto rounded bg-red-900/50 px-2 py-1 text-xs text-red-300 hover:bg-red-900/80 disabled:opacity-50"
              >
                Löschen
              </button>
            </div>

            {replacingKeyFor === destination.id && (
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  submitNewKey(destination);
                }}
                className="flex items-center gap-2"
              >
                <input
                  type="password"
                  autoFocus
                  value={newKeyValue}
                  onChange={(e) => setNewKeyValue(e.target.value)}
                  placeholder="Neuer Stream-Key"
                  aria-label="Neuer Stream-Key"
                  className="flex-1 rounded bg-slate-900 px-2 py-1 text-xs outline-none focus:ring-1 focus:ring-blue-500"
                />
                <button
                  type="submit"
                  disabled={busyDestinationId === destination.id}
                  className="rounded bg-blue-600 px-2 py-1 text-xs hover:bg-blue-500 disabled:opacity-50"
                >
                  Speichern
                </button>
              </form>
            )}

            {rowError?.id === destination.id && <p className="text-xs text-red-400">{rowError.message}</p>}
          </li>
        ))}
      </ul>

      <form onSubmit={submitAdd} data-testid="destination-add-form" className="space-y-2 border-t border-slate-800 pt-3">
        <div className="flex flex-wrap gap-2">
          <select
            value={platform}
            onChange={(e) => changePlatform(e.target.value as StreamPlatform)}
            aria-label="Plattform"
            className="rounded bg-slate-800 px-2 py-1.5 text-sm outline-none focus:ring-1 focus:ring-blue-500"
          >
            <option value="Twitch">Twitch</option>
            <option value="YouTube">YouTube</option>
            <option value="Custom">Benutzerdefiniert</option>
          </select>
          <input
            value={rtmpUrl}
            onChange={(e) => setRtmpUrl(e.target.value)}
            placeholder="rtmp://..."
            aria-label="RTMP-Server-URL"
            className="flex-1 rounded bg-slate-800 px-2 py-1.5 text-sm outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>
        <input
          type="password"
          value={streamKey}
          onChange={(e) => setStreamKey(e.target.value)}
          placeholder="Stream-Key"
          aria-label="Stream-Key"
          className="w-full rounded bg-slate-800 px-2 py-1.5 text-sm outline-none focus:ring-1 focus:ring-blue-500"
        />
        {addError && <p className="text-xs text-red-400">{addError}</p>}
        <button
          type="submit"
          disabled={adding}
          className="w-full rounded bg-blue-600 px-3 py-1.5 text-sm font-medium hover:bg-blue-500 disabled:opacity-50"
        >
          {adding ? 'Wird hinzugefügt…' : 'Destination hinzufügen'}
        </button>
      </form>
    </div>
  );
}
