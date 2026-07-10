/**
 * Central place for URLs the frontend needs to reach the media plane (MediaMTX).
 *
 * RTMP/SRT ingest are raw TCP/UDP protocols, not HTTP, so they can't be routed through the
 * nginx reverse proxy the way /api, /hubs and /hls are - the encoder has to connect to the
 * public host directly. Rather than hardcoding that host (an IP or domain that differs per
 * deployment), it is read from window.location.hostname - the same host the browser used to
 * load the dashboard. The ports are structural: fixed 1:1 in docker-compose.yml's mediamtx
 * service, not secrets or deployment-specific values.
 *
 * The HLS preview intentionally does NOT go through this - it's served relative to the
 * dashboard's own origin (see buildHlsPreviewUrl) via the nginx /hls/ location, so the browser
 * never has to deal with a cross-origin request/cookie at all.
 */
const RTMP_PORT = 1935;
const SRT_PORT = 8890;

function publicHost(): string {
  return window.location.hostname;
}

export function buildRtmpUrl(streamKey: string): string {
  return `rtmp://${publicHost()}:${RTMP_PORT}/live/${streamKey}`;
}

export function buildSrtUrl(streamKey: string): string {
  return `srt://${publicHost()}:${SRT_PORT}?streamid=publish:${streamKey}`;
}

/** Same-origin HLS preview URL, proxied to MediaMTX by deploy/nginx/nginx.conf's /hls/ location. */
export function buildHlsPreviewUrl(streamKey: string): string {
  return `/hls/live/${streamKey}/index.m3u8`;
}
