using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Channels.Dtos;

public sealed record StreamDestinationDto(Guid Id, StreamPlatform Platform, string RtmpUrl, bool IsEnabled, short DisplayOrder);

public sealed record ChannelDto(
    Guid Id,
    string Name,
    string Slug,
    string StreamKey,
    IngestProtocol IngestProtocols,
    bool IsActive,
    SceneState CurrentSceneState,
    SceneThresholds SceneThresholds,
    CompositorStrategy CompositorStrategy,
    IReadOnlyList<StreamDestinationDto> Destinations,
    DateTimeOffset CreatedAt)
{
    public static ChannelDto FromEntity(Channel channel) => new(
        channel.Id,
        channel.Name,
        channel.Slug,
        channel.StreamKey,
        channel.IngestProtocols,
        channel.IsActive,
        channel.CurrentSceneState,
        channel.SceneThresholds,
        channel.CompositorStrategy,
        channel.Destinations
            .OrderBy(d => d.DisplayOrder)
            .Select(d => new StreamDestinationDto(d.Id, d.Platform, d.RtmpUrl, d.IsEnabled, d.DisplayOrder))
            .ToList(),
        channel.CreatedAt);
}
