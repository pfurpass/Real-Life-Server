import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DestinationManager } from './DestinationManager';
import { ChannelsApi } from '../api/endpoints';
import type { StreamDestinationDto } from '../types';

vi.mock('../api/endpoints', () => ({
  ChannelsApi: {
    addDestination: vi.fn(),
    removeDestination: vi.fn(),
    setDestinationEnabled: vi.fn(),
    replaceDestinationKey: vi.fn(),
    reorderDestinations: vi.fn()
  }
}));

function axiosErrorWithTitle(title: string) {
  return Object.assign(new Error(title), { isAxiosError: true, response: { data: { title } } });
}

function destination(overrides: Partial<StreamDestinationDto> = {}): StreamDestinationDto {
  return {
    id: 'dest-1',
    platform: 'Twitch',
    rtmpUrl: 'rtmp://live.twitch.tv/app',
    isEnabled: true,
    displayOrder: 0,
    ...overrides
  };
}

describe('DestinationManager', () => {
  const onReload = vi.fn().mockResolvedValue(undefined);

  beforeEach(() => {
    vi.clearAllMocks();
    onReload.mockResolvedValue(undefined);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('renders destinations sorted by displayOrder and never shows the real key', () => {
    const destinations = [
      destination({ id: 'b', displayOrder: 1, platform: 'YouTube', rtmpUrl: 'rtmp://a.rtmp.youtube.com/live2' }),
      destination({ id: 'a', displayOrder: 0, platform: 'Twitch', rtmpUrl: 'rtmp://live.twitch.tv/app' })
    ];
    render(<DestinationManager channelId="ch-1" destinations={destinations} onReload={onReload} />);

    const rows = screen.getAllByTestId(/^destination-row-/);
    expect(rows.map((r) => r.getAttribute('data-testid'))).toEqual(['destination-row-a', 'destination-row-b']);
    expect(screen.getByTestId('destination-key-a')).toHaveTextContent('••••••••');
    expect(screen.getByTestId('destination-key-b')).toHaveTextContent('••••••••');
  });

  it('submits a new Twitch destination and reloads on success', async () => {
    vi.mocked(ChannelsApi.addDestination).mockResolvedValue({} as never);
    render(<DestinationManager channelId="ch-1" destinations={[]} onReload={onReload} />);

    fireEvent.change(screen.getByLabelText('Stream-Key'), { target: { value: 'my-secret-key' } });
    fireEvent.submit(screen.getByTestId('destination-add-form'));

    await waitFor(() => expect(ChannelsApi.addDestination).toHaveBeenCalledWith('ch-1', 'Twitch', 'rtmp://live.twitch.tv/app', 'my-secret-key'));
    expect(onReload).toHaveBeenCalled();
  });

  it('rejects an invalid RTMP URL client-side without calling the API', async () => {
    render(<DestinationManager channelId="ch-1" destinations={[]} onReload={onReload} />);

    fireEvent.change(screen.getByLabelText('RTMP-Server-URL'), { target: { value: 'http://not-rtmp.example' } });
    fireEvent.change(screen.getByLabelText('Stream-Key'), { target: { value: 'key' } });
    fireEvent.submit(screen.getByTestId('destination-add-form'));

    expect(await screen.findByText(/gültige rtmp:\/\/ oder rtmps:\/\/ URL/)).toBeInTheDocument();
    expect(ChannelsApi.addDestination).not.toHaveBeenCalled();
  });

  it('surfaces the backend validation message on a rejected add', async () => {
    vi.mocked(ChannelsApi.addDestination).mockRejectedValue(axiosErrorWithTitle('"ftp://x" is not a valid rtmp:// or rtmps:// URL.'));
    render(<DestinationManager channelId="ch-1" destinations={[]} onReload={onReload} />);

    fireEvent.change(screen.getByLabelText('Stream-Key'), { target: { value: 'key' } });
    fireEvent.submit(screen.getByTestId('destination-add-form'));

    expect(await screen.findByText('"ftp://x" is not a valid rtmp:// or rtmps:// URL.')).toBeInTheDocument();
    expect(onReload).not.toHaveBeenCalled();
  });

  it('toggles a destination enabled/disabled', async () => {
    vi.mocked(ChannelsApi.setDestinationEnabled).mockResolvedValue({} as never);
    const destinations = [destination({ isEnabled: true })];
    render(<DestinationManager channelId="ch-1" destinations={destinations} onReload={onReload} />);

    fireEvent.click(screen.getByRole('button', { name: 'Deaktivieren' }));

    await waitFor(() => expect(ChannelsApi.setDestinationEnabled).toHaveBeenCalledWith('ch-1', 'dest-1', false));
    expect(onReload).toHaveBeenCalled();
  });

  it('deletes only after the confirmation dialog is accepted', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const destinations = [destination()];
    render(<DestinationManager channelId="ch-1" destinations={destinations} onReload={onReload} />);

    fireEvent.click(screen.getByRole('button', { name: 'Löschen' }));

    expect(window.confirm).toHaveBeenCalled();
    expect(ChannelsApi.removeDestination).not.toHaveBeenCalled();
  });

  it('deletes when the confirmation dialog is accepted', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.mocked(ChannelsApi.removeDestination).mockResolvedValue({} as never);
    const destinations = [destination()];
    render(<DestinationManager channelId="ch-1" destinations={destinations} onReload={onReload} />);

    fireEvent.click(screen.getByRole('button', { name: 'Löschen' }));

    await waitFor(() => expect(ChannelsApi.removeDestination).toHaveBeenCalledWith('ch-1', 'dest-1'));
    expect(onReload).toHaveBeenCalled();
  });

  it('reorders by moving a destination down, sending the full new order', async () => {
    vi.mocked(ChannelsApi.reorderDestinations).mockResolvedValue({} as never);
    const destinations = [destination({ id: 'a', displayOrder: 0 }), destination({ id: 'b', displayOrder: 1 })];
    render(<DestinationManager channelId="ch-1" destinations={destinations} onReload={onReload} />);

    const rowA = screen.getByTestId('destination-row-a');
    fireEvent.click(within(rowA).getByRole('button', { name: 'Nach unten verschieben' }));

    await waitFor(() => expect(ChannelsApi.reorderDestinations).toHaveBeenCalledWith('ch-1', ['b', 'a']));
    expect(onReload).toHaveBeenCalled();
  });

  it('replaces a stream key via the inline form', async () => {
    vi.mocked(ChannelsApi.replaceDestinationKey).mockResolvedValue({} as never);
    const destinations = [destination()];
    render(<DestinationManager channelId="ch-1" destinations={destinations} onReload={onReload} />);

    fireEvent.click(screen.getByRole('button', { name: 'Key ersetzen' }));
    fireEvent.change(screen.getByLabelText('Neuer Stream-Key'), { target: { value: 'brand-new-key' } });
    fireEvent.click(screen.getByRole('button', { name: 'Speichern' }));

    await waitFor(() => expect(ChannelsApi.replaceDestinationKey).toHaveBeenCalledWith('ch-1', 'dest-1', 'brand-new-key'));
    expect(onReload).toHaveBeenCalled();
  });
});
