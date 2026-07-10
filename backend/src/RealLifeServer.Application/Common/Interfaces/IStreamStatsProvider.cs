namespace RealLifeServer.Application.Common.Interfaces;

public sealed record ChannelIngestStats(
    bool IsPublishing,
    int BitrateKbps,
    double PacketLossPercent,
    double FpsIn);

/// <summary>
/// Reads live ingest telemetry for a channel from the media gateway (MediaMTX by default).
/// Swappable per docs/CONCEPT.md chapter 13 (e.g. an SRS or Cloudflare Stream adapter).
/// </summary>
public interface IStreamStatsProvider
{
    Task<ChannelIngestStats> GetChannelStatsAsync(string streamKey, CancellationToken ct = default);
}
