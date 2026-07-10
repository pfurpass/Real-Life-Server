import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LivePreviewPlayer } from './LivePreviewPlayer';

// hls.js needs real Media Source Extensions, which jsdom doesn't implement - a minimal fake
// standing in for the real class lets the component's own wiring (event registration, teardown,
// recovery calls) be exercised directly instead of guessed at. Everything the mock factory below
// touches has to live inside vi.hoisted - vi.mock calls are hoisted above regular top-level code.
const { FakeHls, instances, isSupportedMock } = vi.hoisted(() => {
  type Handler = (event: string, data?: unknown) => void;
  const instances: InstanceType<typeof FakeHlsImpl>[] = [];
  const isSupportedMock = vi.fn(() => true);

  class FakeHlsImpl {
    static isSupported = isSupportedMock;
    static Events = {
      MEDIA_ATTACHED: 'hlsMediaAttached',
      MANIFEST_PARSED: 'hlsManifestParsed',
      ERROR: 'hlsError',
      FRAG_BUFFERED: 'hlsFragBuffered'
    };
    static ErrorTypes = { NETWORK_ERROR: 'networkError', MEDIA_ERROR: 'mediaError', OTHER_ERROR: 'otherError' };
    static ErrorDetails = { MANIFEST_LOAD_ERROR: 'manifestLoadError' };

    handlers: Record<string, Handler[]> = {};
    destroy = vi.fn();
    loadSource = vi.fn();
    attachMedia = vi.fn();
    startLoad = vi.fn();
    recoverMediaError = vi.fn();

    constructor() {
      instances.push(this);
    }

    on(event: string, cb: Handler) {
      (this.handlers[event] ??= []).push(cb);
    }

    emit(event: string, data?: unknown) {
      (this.handlers[event] ?? []).forEach((cb) => cb(event, data));
    }
  }

  return { FakeHls: FakeHlsImpl, instances, isSupportedMock };
});

vi.mock('hls.js', () => ({ default: FakeHls }));

function mockManifestFetch(response: { ok: boolean; status: number }) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({ ok: response.ok, status: response.status } as Response)
  );
}

describe('LivePreviewPlayer', () => {
  beforeEach(() => {
    instances.length = 0;
    isSupportedMock.mockReturnValue(true);
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('uses hls.js when the browser supports it', async () => {
    mockManifestFetch({ ok: true, status: 200 });
    render(<LivePreviewPlayer streamKey="abc123" />);

    await waitFor(() => expect(instances).toHaveLength(1));
    expect(instances[0].loadSource).toHaveBeenCalledWith('/hls/live/abc123/index.m3u8');
    expect(instances[0].attachMedia).toHaveBeenCalled();
  });

  it('falls back to native HLS playback when hls.js is not supported', async () => {
    isSupportedMock.mockReturnValue(false);
    vi.spyOn(HTMLMediaElement.prototype, 'canPlayType').mockReturnValue('maybe');
    mockManifestFetch({ ok: true, status: 200 });

    render(<LivePreviewPlayer streamKey="abc123" />);

    await waitFor(() => {
      const video = screen.getByTestId('live-preview-video') as HTMLVideoElement;
      expect(video.src).toContain('/hls/live/abc123/index.m3u8');
    });
    expect(instances).toHaveLength(0);
  });

  it('destroys the Hls instance on cleanup', async () => {
    mockManifestFetch({ ok: true, status: 200 });
    const { unmount } = render(<LivePreviewPlayer streamKey="abc123" />);

    await waitFor(() => expect(instances).toHaveLength(1));
    unmount();

    expect(instances[0].destroy).toHaveBeenCalled();
  });

  it('destroys the old instance and loads the new stream on a stream-key change', async () => {
    mockManifestFetch({ ok: true, status: 200 });
    const { rerender } = render(<LivePreviewPlayer streamKey="key-a" />);

    await waitFor(() => expect(instances).toHaveLength(1));
    const first = instances[0];

    rerender(<LivePreviewPlayer streamKey="key-b" />);

    await waitFor(() => expect(instances).toHaveLength(2));
    expect(first.destroy).toHaveBeenCalled();
    expect(instances[1].loadSource).toHaveBeenCalledWith('/hls/live/key-b/index.m3u8');
  });

  it('shows the offline state and never instantiates a player when the manifest 404s', async () => {
    mockManifestFetch({ ok: false, status: 404 });
    render(<LivePreviewPlayer streamKey="abc123" />);

    await waitFor(() => {
      expect(screen.getByTestId('live-preview-overlay')).toHaveAttribute('data-preview-state', 'offline');
    });
    expect(instances).toHaveLength(0);
  });

  it('shows the live state (no overlay) once the manifest is parsed', async () => {
    mockManifestFetch({ ok: true, status: 200 });
    render(<LivePreviewPlayer streamKey="abc123" />);

    await waitFor(() => expect(instances).toHaveLength(1));
    act(() => instances[0].emit(FakeHls.Events.MANIFEST_PARSED));

    await waitFor(() => expect(screen.queryByTestId('live-preview-overlay')).not.toBeInTheDocument());
  });

  it('recovers from a fatal network error, then gives up after repeated failures', async () => {
    mockManifestFetch({ ok: true, status: 200 });
    render(<LivePreviewPlayer streamKey="abc123" />);

    await waitFor(() => expect(instances).toHaveLength(1));
    const hls = instances[0];

    const fatalNetworkError = { fatal: true, type: FakeHls.ErrorTypes.NETWORK_ERROR, details: 'fragLoadError' };
    act(() => hls.emit(FakeHls.Events.ERROR, fatalNetworkError));
    act(() => hls.emit(FakeHls.Events.ERROR, fatalNetworkError));
    act(() => hls.emit(FakeHls.Events.ERROR, fatalNetworkError));
    expect(hls.startLoad).toHaveBeenCalledTimes(3);
    expect(hls.destroy).not.toHaveBeenCalled();

    act(() => hls.emit(FakeHls.Events.ERROR, fatalNetworkError));

    await waitFor(() => {
      expect(screen.getByTestId('live-preview-overlay')).toHaveAttribute('data-preview-state', 'manifest-unreachable');
    });
    expect(hls.destroy).toHaveBeenCalled();
  });

  it('recovers from a fatal media error, then gives up after repeated failures', async () => {
    mockManifestFetch({ ok: true, status: 200 });
    render(<LivePreviewPlayer streamKey="abc123" />);

    await waitFor(() => expect(instances).toHaveLength(1));
    const hls = instances[0];

    const fatalMediaError = { fatal: true, type: FakeHls.ErrorTypes.MEDIA_ERROR, details: 'bufferStalledError' };
    act(() => hls.emit(FakeHls.Events.ERROR, fatalMediaError));
    act(() => hls.emit(FakeHls.Events.ERROR, fatalMediaError));
    act(() => hls.emit(FakeHls.Events.ERROR, fatalMediaError));
    expect(hls.recoverMediaError).toHaveBeenCalledTimes(3);
    expect(hls.destroy).not.toHaveBeenCalled();

    act(() => hls.emit(FakeHls.Events.ERROR, fatalMediaError));

    await waitFor(() => {
      expect(screen.getByTestId('live-preview-overlay')).toHaveAttribute('data-preview-state', 'playback-error');
    });
    expect(hls.destroy).toHaveBeenCalled();
  });
});
