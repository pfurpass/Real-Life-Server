using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealLifeServer.Infrastructure.Streaming;

public sealed record MediaMtxPathInfo(bool Ready, long BytesReceived);

internal sealed record MediaMtxPathListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MediaMtxPathListItem>? Items);

internal sealed record MediaMtxPathListItem(
    [property: JsonPropertyName("name")] string Name,
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
            // Deliberately not /v3/paths/get/{name}: that endpoint requires the path name (which
            // contains a literal '/') to be percent-encoded into a single URL path segment
            // (.../get/live%2Fkey), and MediaMTX v1.19.2's router does not reliably accept %2F
            // there. /v3/paths/list + filtering client-side avoids the ambiguity entirely.
            var response = await httpClient.GetFromJsonAsync<MediaMtxPathListResponse>(
                $"{options.Value.ApiBaseUrl}/v3/paths/list", ct);

            var item = response?.Items?.FirstOrDefault(i => i.Name == pathName);
            return item is null ? null : new MediaMtxPathInfo(item.Ready, item.BytesReceived);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "MediaMTX path list lookup failed");
            return null;
        }
    }
}
