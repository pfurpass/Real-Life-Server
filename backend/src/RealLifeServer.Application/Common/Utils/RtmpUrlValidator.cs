namespace RealLifeServer.Application.Common.Utils;

/// <summary>Validates outbound destination server URLs (Twitch/YouTube/custom RTMP ingest).</summary>
public static class RtmpUrlValidator
{
    public static bool IsValid(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme is "rtmp" or "rtmps")
        && !string.IsNullOrWhiteSpace(uri.Host);
}
