// Mirrors the backend DTOs (backend/src/RealLifeServer.Application/**/Dtos). Keep in sync
// manually - see docs/CONCEPT.md chapter 9 for the REST API surface these are shaped from.

export type UserRole = 'Admin' | 'Moderator' | 'Streamer';

export type SceneState = 'Offline' | 'Connecting' | 'Live' | 'Degraded' | 'Reconnecting' | 'Brb';

export type SceneTrigger =
  | 'EncoderConnected'
  | 'FirstKeyframeReceived'
  | 'ConnectTimeoutElapsed'
  | 'BitrateBelowThreshold'
  | 'PacketLossAboveThreshold'
  | 'MetricsRecovered'
  | 'EncoderDisconnected'
  | 'ReconnectTimeoutElapsed'
  | 'ManualStop'
  | 'ChannelSuspended'
  | 'ManualOverride';

export type StreamPlatform = 'Twitch' | 'YouTube' | 'Custom';

export const IngestProtocol = {
  None: 0,
  Rtmp: 1,
  Srt: 2,
  Whip: 4
} as const;

export interface SceneThresholds {
  heartbeatTimeoutSeconds: number;
  reconnectTimeoutSeconds: number;
  minBitrateKbps: number;
  maxPacketLossPercent: number;
  recoverySampleCount: number;
  connectTimeoutSeconds: number;
}

export interface StreamDestinationDto {
  id: string;
  platform: StreamPlatform;
  rtmpUrl: string;
  isEnabled: boolean;
  displayOrder: number;
}

export interface ChannelDto {
  id: string;
  name: string;
  slug: string;
  streamKey: string;
  ingestProtocols: number;
  isActive: boolean;
  currentSceneState: SceneState;
  sceneThresholds: SceneThresholds;
  compositorStrategy: number;
  destinations: StreamDestinationDto[];
  createdAt: string;
}

export interface StreamStatusDto {
  channelId: string;
  sceneState: SceneState;
  lastBitrateKbps: number | null;
  lastPacketLossPercent: number | null;
  lastHeartbeatAt: string | null;
  encoderConnected: boolean;
}

export interface MetricSampleDto {
  recordedAt: string;
  bitrateKbps: number;
  packetLossPercent: number;
  fpsIn: number;
  cpuUsagePercent: number;
  ramUsagePercent: number;
  networkUsageMbps: number;
}

export interface SceneEventDto {
  id: string;
  fromState: SceneState;
  toState: SceneState;
  trigger: SceneTrigger;
  occurredAt: string;
  metadataJson: string | null;
}

export interface HostSystemStats {
  cpuUsagePercent: number;
  ramUsagePercent: number;
  networkUsageMbps: number;
}

export interface AuthResult {
  userId: string;
  username: string;
  role: UserRole;
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
}
