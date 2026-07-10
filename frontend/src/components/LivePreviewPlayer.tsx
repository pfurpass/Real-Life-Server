import { useEffect, useRef, useState } from 'react';
import Hls from 'hls.js';

/**
 * Web preview of the compositor output. MediaMTX exposes low-latency HLS for every path
 * (docs/CONCEPT.md, chapter 4.4); hls.js is used for broad browser compatibility, with a
 * plain <video> fallback for Safari, which supports HLS natively.
 */
export function LivePreviewPlayer({ streamKey }: { streamKey: string }) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) {
      return;
    }

    const src = `/hls/live/${streamKey}/index.m3u8`;
    setError(null);

    if (Hls.isSupported()) {
      const hls = new Hls({ lowLatencyMode: true });
      hls.loadSource(src);
      hls.attachMedia(video);
      hls.on(Hls.Events.ERROR, (_evt, data) => {
        if (data.fatal) {
          setError('Vorschau nicht verfügbar (kein aktiver Stream?)');
        }
      });
      return () => hls.destroy();
    }

    if (video.canPlayType('application/vnd.apple.mpegurl')) {
      video.src = src;
    } else {
      setError('HLS wird von diesem Browser nicht unterstützt.');
    }
  }, [streamKey]);

  return (
    <div className="relative aspect-video w-full overflow-hidden rounded-lg bg-black">
      <video ref={videoRef} autoPlay muted controls playsInline className="h-full w-full" />
      {error && (
        <div className="absolute inset-0 flex items-center justify-center bg-black/70 text-sm text-slate-300">
          {error}
        </div>
      )}
    </div>
  );
}
