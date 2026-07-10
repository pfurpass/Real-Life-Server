using RealLifeServer.Domain.Common;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Domain.Entities;

/// <summary>An outbound restream target (Twitch, YouTube, custom RTMP) for a channel.</summary>
public class StreamDestination : BaseEntity
{
    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }

    public StreamPlatform Platform { get; set; }
    public string RtmpUrl { get; set; } = string.Empty;

    /// <summary>AES-256-GCM encrypted at rest. Decrypted only in-memory when building the FFmpeg command.</summary>
    public string StreamKeyEncrypted { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public short DisplayOrder { get; set; }
}
