using RealLifeServer.Domain.Common;

namespace RealLifeServer.Domain.Entities;

/// <summary>
/// Records that "the encoder was alive at time T", sourced either from MediaMTX webhooks
/// or from an optional companion agent on the encoder side. See docs/CONCEPT.md, chapter 13.
/// </summary>
public class HeartbeatRecord : BaseEntity
{
    public Guid ChannelId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>"mediamtx-webhook" | "encoder-agent".</summary>
    public string Source { get; set; } = "mediamtx-webhook";
}
