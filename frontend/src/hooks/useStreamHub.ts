import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { getAccessToken } from '../api/client';
import type { SceneState, SceneTrigger } from '../types';

export interface SceneChangedEvent {
  channelId: string;
  fromState: SceneState;
  toState: SceneState;
  trigger: SceneTrigger;
  occurredAt: string;
}

export interface MetricsUpdatedEvent {
  channelId: string;
  bitrateKbps: number;
  packetLossPercent: number;
  fpsIn: number;
  timestamp: string;
}

export interface LogAppendedEvent {
  channelId: string;
  level: string;
  message: string;
  timestamp: string;
}

/**
 * Joins the SignalR group for one channel (docs/CONCEPT.md, chapter 8) and exposes the latest
 * scene-change / metrics events as React state, so dashboard widgets update in real time
 * without polling.
 */
export function useStreamHub(channelId: string | null) {
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const [lastSceneChange, setLastSceneChange] = useState<SceneChangedEvent | null>(null);
  const [lastMetrics, setLastMetrics] = useState<MetricsUpdatedEvent | null>(null);
  const [logs, setLogs] = useState<LogAppendedEvent[]>([]);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (!channelId) {
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/streams', { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .build();

    connection.on('SceneChanged', (event: SceneChangedEvent) => {
      if (event.channelId === channelId) {
        setLastSceneChange(event);
      }
    });

    connection.on('MetricsUpdated', (event: MetricsUpdatedEvent) => {
      if (event.channelId === channelId) {
        setLastMetrics(event);
      }
    });

    connection.on('LogAppended', (event: LogAppendedEvent) => {
      if (event.channelId === channelId) {
        setLogs((prev) => [event, ...prev].slice(0, 200));
      }
    });

    connection
      .start()
      .then(() => {
        setConnected(true);
        return connection.invoke('JoinChannel', channelId);
      })
      .catch(() => setConnected(false));

    connectionRef.current = connection;

    return () => {
      connection.stop();
      connectionRef.current = null;
      setConnected(false);
    };
  }, [channelId]);

  return { lastSceneChange, lastMetrics, logs, connected };
}
