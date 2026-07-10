import { apiClient } from './client';
import type {
  AuthResult,
  ChannelDto,
  HostSystemStats,
  MetricSampleDto,
  SceneEventDto,
  SceneState,
  SceneThresholds,
  StreamPlatform,
  StreamStatusDto
} from '../types';

export const AuthApi = {
  login: (username: string, password: string) =>
    apiClient.post<AuthResult>('/auth/login', { username, password }).then((r) => r.data)
};

export const ChannelsApi = {
  list: () => apiClient.get<ChannelDto[]>('/channels').then((r) => r.data),
  get: (id: string) => apiClient.get<ChannelDto>(`/channels/${id}`).then((r) => r.data),
  create: (name: string, ingestProtocols: number) =>
    apiClient.post<ChannelDto>('/channels', { name, ingestProtocols }).then((r) => r.data),
  update: (id: string, name: string, isActive: boolean, thresholds: SceneThresholds) =>
    apiClient.put<ChannelDto>(`/channels/${id}`, { name, isActive, thresholds }).then((r) => r.data),
  remove: (id: string) => apiClient.delete(`/channels/${id}`),
  regenerateKey: (id: string) => apiClient.post<ChannelDto>(`/channels/${id}/regenerate-key`).then((r) => r.data),
  addDestination: (id: string, platform: StreamPlatform, rtmpUrl: string, streamKey: string) =>
    apiClient.post<ChannelDto>(`/channels/${id}/destinations`, { platform, rtmpUrl, streamKey }).then((r) => r.data),
  removeDestination: (id: string, destinationId: string) =>
    apiClient.delete(`/channels/${id}/destinations/${destinationId}`),
  setDestinationEnabled: (id: string, destinationId: string, isEnabled: boolean) =>
    apiClient.put<ChannelDto>(`/channels/${id}/destinations/${destinationId}/enabled`, { isEnabled }).then((r) => r.data),
  replaceDestinationKey: (id: string, destinationId: string, streamKey: string) =>
    apiClient.post<ChannelDto>(`/channels/${id}/destinations/${destinationId}/stream-key`, { streamKey }).then((r) => r.data),
  reorderDestinations: (id: string, orderedDestinationIds: string[]) =>
    apiClient.put<ChannelDto>(`/channels/${id}/destinations/order`, { orderedDestinationIds }).then((r) => r.data)
};

export const StreamsApi = {
  status: (channelId: string) => apiClient.get<StreamStatusDto>(`/streams/${channelId}/status`).then((r) => r.data),
  metrics: (channelId: string, rangeMinutes: number) =>
    apiClient.get<MetricSampleDto[]>(`/streams/${channelId}/metrics`, { params: { rangeMinutes } }).then((r) => r.data),
  events: (channelId: string, take = 100) =>
    apiClient.get<SceneEventDto[]>(`/streams/${channelId}/events`, { params: { take } }).then((r) => r.data),
  forceScene: (channelId: string, targetState: SceneState) =>
    apiClient.post(`/streams/${channelId}/force-scene`, { targetState }),
  stop: (channelId: string) => apiClient.post(`/streams/${channelId}/stop`)
};

export const SystemApi = {
  stats: () => apiClient.get<HostSystemStats>('/system/stats').then((r) => r.data)
};
