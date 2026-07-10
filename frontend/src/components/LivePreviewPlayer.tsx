import { useEffect, useRef, useState } from 'react';
import Hls, { type ErrorData } from 'hls.js';
import { buildHlsPreviewUrl } from '../config/serverConfig';

/**
 * Web preview of the compositor output. MediaMTX exposes low-latency HLS for every path
 * (docs/CONCEPT.md, chapter 4.4): hls.js is used for broad browser compatibility via Media
 * Source Extensions (Chrome/Firefox/Edge have no native HLS support at all), with native
 * <video> playback for Safari, which handles HLS itself.
 *
 * "No active stream" (manifest 404 - a normal, expected state) is deliberately distinguished
 * from an actual technical failure (network/proxy/codec problem) instead of collapsing both
 * into one generic "preview unavailable" message.
 */
type PreviewState = 'loading' | 'offline' | 'manifest-unreachable' | 'playback-error' | 'live';

const OFFLINE_POLL_INTERVAL_MS = 5000;
const MAX_NETWORK_RECOVERY_ATTEMPTS = 3;
const MAX_MEDIA_RECOVERY_ATTEMPTS = 3;

const STATE_LABELS: Record<Exclude<PreviewState, 'live'>, string> = {
  loading: 'Stream wird geladen…',
  offline: 'Kein aktiver Stream.',
  'manifest-unreachable': 'HLS-Manifest nicht erreichbar.',
  'playback-error': 'Wiedergabefehler bei der Vorschau.'
};

function logHlsError(streamKey: string, data: ErrorData): void {
  console.error('[LivePreviewPlayer] hls.js error', {
    streamKey,
    type: data.type,
    details: data.details,
    fatal: data.fatal,
    reason: data.reason,
    responseCode: data.response?.code
  });
}

export function LivePreviewPlayer({ streamKey }: { streamKey: string }) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [state, setState] = useState<PreviewState>('loading');

  useEffect(() => {
    if (!videoRef.current) {
      return;
    }
    // Captured as a non-null local: TS's narrowing of videoRef.current doesn't survive into the
    // nested `start()` closure below, and videoRef.current itself can't be reassigned here anyway.
    const el: HTMLVideoElement = videoRef.current;

    const src = buildHlsPreviewUrl(streamKey);
    let cancelled = false;
    let hls: Hls | null = null;
    let pollTimer: ReturnType<typeof setTimeout> | undefined;
    let networkRecoveryAttempts = 0;
    let mediaRecoveryAttempts = 0;
    let nativeListeners: { loadedmetadata: () => void; error: () => void } | null = null;

    const scheduleOfflinePoll = () => {
      pollTimer = setTimeout(() => {
        if (!cancelled) {
          start();
        }
      }, OFFLINE_POLL_INTERVAL_MS);
    };

    const teardownPlayer = () => {
      hls?.destroy();
      hls = null;
      if (nativeListeners) {
        el.removeEventListener('loadedmetadata', nativeListeners.loadedmetadata);
        el.removeEventListener('error', nativeListeners.error);
        nativeListeners = null;
      }
      el.removeAttribute('src');
      el.load();
    };

    const enterOffline = () => {
      teardownPlayer();
      if (!cancelled) {
        setState('offline');
      }
      scheduleOfflinePoll();
    };

    const enterUnreachable = () => {
      teardownPlayer();
      if (!cancelled) {
        setState('manifest-unreachable');
      }
      scheduleOfflinePoll();
    };

    const enterPlaybackError = () => {
      teardownPlayer();
      if (!cancelled) {
        setState('playback-error');
      }
    };

    async function start() {
      setState('loading');
      networkRecoveryAttempts = 0;
      mediaRecoveryAttempts = 0;

      // Probed up front (rather than only inferred from hls.js's own manifest-load error) so the
      // native-Safari fallback path gets the same offline/unreachable distinction as hls.js does.
      let manifestReachable = false;
      let manifestMissing = false;
      try {
        const response = await fetch(src, { method: 'GET', cache: 'no-store' });
        manifestReachable = response.ok;
        manifestMissing = response.status === 404;
      } catch (err) {
        console.error('[LivePreviewPlayer] manifest probe failed', { streamKey, error: err });
      }

      if (cancelled) {
        return;
      }
      if (manifestMissing) {
        setState('offline');
        scheduleOfflinePoll();
        return;
      }
      if (!manifestReachable) {
        setState('manifest-unreachable');
        scheduleOfflinePoll();
        return;
      }

      if (Hls.isSupported()) {
        hls = new Hls({ lowLatencyMode: true });

        hls.on(Hls.Events.MEDIA_ATTACHED, () => {
          console.info('[LivePreviewPlayer] media attached', { streamKey });
        });

        hls.on(Hls.Events.MANIFEST_PARSED, () => {
          if (cancelled) {
            return;
          }
          setState('live');
          el.play().catch(() => {
            // Autoplay can still be blocked by the browser even when muted; the visible controls
            // let the user start it manually - that's not a stream/player error.
          });
        });

        hls.on(Hls.Events.FRAG_BUFFERED, () => {
          networkRecoveryAttempts = 0;
          mediaRecoveryAttempts = 0;
        });

        hls.on(Hls.Events.ERROR, (_evt, data) => {
          logHlsError(streamKey, data);
          if (!data.fatal) {
            return;
          }

          if (data.details === Hls.ErrorDetails.MANIFEST_LOAD_ERROR && data.response?.code === 404) {
            enterOffline();
            return;
          }

          switch (data.type) {
            case Hls.ErrorTypes.NETWORK_ERROR:
              networkRecoveryAttempts += 1;
              if (networkRecoveryAttempts <= MAX_NETWORK_RECOVERY_ATTEMPTS) {
                hls?.startLoad();
              } else {
                enterUnreachable();
              }
              return;
            case Hls.ErrorTypes.MEDIA_ERROR:
              mediaRecoveryAttempts += 1;
              if (mediaRecoveryAttempts <= MAX_MEDIA_RECOVERY_ATTEMPTS) {
                hls?.recoverMediaError();
              } else {
                enterPlaybackError();
              }
              return;
            default:
              enterPlaybackError();
          }
        });

        hls.loadSource(src);
        hls.attachMedia(el);
        return;
      }

      if (el.canPlayType('application/vnd.apple.mpegurl')) {
        const onLoadedMetadata = () => {
          if (!cancelled) {
            setState('live');
          }
        };
        const onError = () => {
          console.error('[LivePreviewPlayer] native HLS playback error', { streamKey });
          if (!cancelled) {
            setState('playback-error');
          }
        };
        nativeListeners = { loadedmetadata: onLoadedMetadata, error: onError };
        el.addEventListener('loadedmetadata', onLoadedMetadata);
        el.addEventListener('error', onError);
        el.src = src;
        el.play().catch(() => {});
        return;
      }

      setState('manifest-unreachable');
    }

    start();

    return () => {
      cancelled = true;
      if (pollTimer) {
        clearTimeout(pollTimer);
      }
      teardownPlayer();
    };
  }, [streamKey]);

  return (
    <div className="relative aspect-video w-full overflow-hidden rounded-lg bg-black">
      <video ref={videoRef} autoPlay muted controls playsInline className="h-full w-full" data-testid="live-preview-video" />
      {state !== 'live' && (
        <div
          data-testid="live-preview-overlay"
          data-preview-state={state}
          className="absolute inset-0 flex items-center justify-center bg-black/70 text-sm text-slate-300"
        >
          {STATE_LABELS[state]}
        </div>
      )}
    </div>
  );
}
