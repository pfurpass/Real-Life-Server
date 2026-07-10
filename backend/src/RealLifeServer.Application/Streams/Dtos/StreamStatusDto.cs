using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.Dtos;

public sealed record StreamStatusDto(
    Guid ChannelId,
    SceneState SceneState,
    int? LastBitrateKbps,
    double? LastPacketLossPercent,
    DateTimeOffset? LastHeartbeatAt,
    bool EncoderConnected);

public sealed record MetricSampleDto(
    DateTimeOffset RecordedAt,
    int BitrateKbps,
    double PacketLossPercent,
    double FpsIn,
    double CpuUsagePercent,
    double RamUsagePercent,
    double NetworkUsageMbps);

public sealed record SceneEventDto(
    Guid Id,
    SceneState FromState,
    SceneState ToState,
    SceneTrigger Trigger,
    DateTimeOffset OccurredAt,
    string? MetadataJson);
