using System.Collections.Concurrent;
using RealLifeServer.Application.Common.Interfaces;

namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Derives ingest bitrate from MediaMTX's cumulative bytesReceived counter (delta / delta-time).
/// Packet loss detection for RTMP/WHIP is not exposed by MediaMTX's HTTP API at the time of
/// writing; for SRT specifically MediaMTX exposes richer connection stats that a future
/// `ISrtStatsAugmenter` could layer on top of this provider (see docs/CONCEPT.md, chapter 13).
/// Until then PacketLossPercent is reported as 0 and the scene state machine relies on the
/// bitrate and heartbeat-timeout thresholds, which cover the "connection is failing" case.
/// </summary>
public class MediaMtxStatsProvider(MediaMtxClient client) : IStreamStatsProvider
{
    private readonly ConcurrentDictionary<string, (long Bytes, DateTimeOffset At)> _lastSample = new();

    public async Task<ChannelIngestStats> GetChannelStatsAsync(string streamKey, CancellationToken ct = default)
    {
        var path = await client.GetPathAsync(streamKey, ct);
        if (path is null || !path.Ready)
        {
            _lastSample.TryRemove(streamKey, out _);
            return new ChannelIngestStats(false, 0, 0, 0);
        }

        var now = DateTimeOffset.UtcNow;
        var bitrateKbps = 0;

        if (_lastSample.TryGetValue(streamKey, out var previous))
        {
            var elapsed = (now - previous.At).TotalSeconds;
            var deltaBytes = path.BytesReceived - previous.Bytes;
            if (elapsed > 0 && deltaBytes >= 0)
            {
                bitrateKbps = (int)(deltaBytes * 8 / 1000 / elapsed);
            }
        }

        _lastSample[streamKey] = (path.BytesReceived, now);

        return new ChannelIngestStats(true, bitrateKbps, PacketLossPercent: 0, FpsIn: 0);
    }
}
