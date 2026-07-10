using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealLifeServer.Infrastructure.Streaming;

public sealed record MediaMtxPathInfo(
    [property: JsonPropertyName("ready")] bool Ready,
    [property: JsonPropertyName("bytesReceived")] long BytesReceived);

/// <summary>Thin HTTP client for the MediaMTX control API (deploy/mediamtx/mediamtx.yml, "api: yes").</summary>
public class MediaMtxClient(HttpClient httpClient, IOptions<MediaMtxOptions> options, ILogger<MediaMtxClient> logger)
{
    public async Task<MediaMtxPathInfo?> GetPathAsync(string streamKey, CancellationToken ct = default)
    {
        var pathName = $"{options.Value.PathPrefix}/{streamKey}";
        try
        {
            return await httpClient.GetFromJsonAsync<MediaMtxPathInfo>(
                $"{options.Value.ApiBaseUrl}/v3/paths/get/{Uri.EscapeDataString(pathName)}", ct);
        }
        catch (HttpRequestException ex)
        {
            // 404 simply means "no such path yet" (nobody ever published to this key) - not an error worth logging loudly.
            logger.LogDebug(ex, "MediaMTX path lookup failed for {PathName}", pathName);
            return null;
        }
    }
}
