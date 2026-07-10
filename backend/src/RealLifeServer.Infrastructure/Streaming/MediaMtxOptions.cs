namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>Bound from configuration section "MediaMtx". Points at the MediaMTX ingest gateway (deploy/mediamtx/mediamtx.yml).</summary>
public class MediaMtxOptions
{
    public string RtspBaseUrl { get; set; } = "rtsp://mediamtx:8554";
    public string ApiBaseUrl { get; set; } = "http://mediamtx:9997";
    public string PathPrefix { get; set; } = "live";
}
