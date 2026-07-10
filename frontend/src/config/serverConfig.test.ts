import { describe, expect, it } from 'vitest';
import { buildHlsPreviewUrl, buildRtmpUrl, buildSrtUrl } from './serverConfig';

describe('serverConfig', () => {
  it('derives the RTMP URL from the browser host, not a hardcoded placeholder', () => {
    const url = buildRtmpUrl('abc123');
    expect(url).toBe(`rtmp://${window.location.hostname}:1935/live/abc123`);
    expect(url).not.toContain('<host>');
  });

  it('derives the SRT URL from the browser host, not a hardcoded placeholder', () => {
    const url = buildSrtUrl('abc123');
    expect(url).toBe(`srt://${window.location.hostname}:8890?streamid=publish:abc123`);
    expect(url).not.toContain('<host>');
  });

  it('builds a same-origin relative HLS preview URL with no host or IP baked in', () => {
    const url = buildHlsPreviewUrl('abc123');
    expect(url).toBe('/hls/live/abc123/index.m3u8');
    expect(url).not.toContain('<host>');
    expect(url).not.toMatch(/^https?:\/\//);
  });
});
