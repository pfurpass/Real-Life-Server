using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RealLifeServer.Application.Streams.Commands;

namespace RealLifeServer.Api.Controllers;

/// <summary>Bound from configuration section "Webhooks". The shared secret MediaMTX sends back on the ready/not-ready callbacks (see deploy/mediamtx/mediamtx.yml).</summary>
public class WebhookOptions
{
    public string Secret { get; set; } = string.Empty;
}

public sealed record MediaMtxAuthRequest(string Path, string Action, string? Ip, string? Protocol);
public sealed record MediaMtxNotificationRequest(string Path);

/// <summary>
/// Endpoints called by MediaMTX, not by the frontend: the synchronous auth gate
/// (`authHTTPAddress`) and the fire-and-forget ready/not-ready notification hooks. See
/// docs/CONCEPT.md, chapter 3 and 10.
///
/// The two hook families are secured differently, because MediaMTX itself gives us different
/// capabilities for each:
///  - `authHTTPAddress` is called *by MediaMTX's own HTTP client* with a fixed JSON body
///    (ip/user/password/token/action/path/protocol/id/query/userAgent) and no configurable
///    headers - there is no way to make it send a shared secret. Its safety instead comes from
///    (a) validating the real business fact - is this stream key valid and active - for every
///    "publish" action, and (b) only being reachable on the internal Docker network
///    (docker-compose.yml does not publish the api container's port; deploy/nginx/nginx.conf
///    explicitly blocks the /api/webhooks/ prefix from the public reverse proxy).
///  - `runOnReady`/`runOnNotReady` are shell commands *we* author in mediamtx.yml, so they can
///    (and do) send a X-Webhook-Secret header we control end to end.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks/mediamtx")]
[EnableRateLimiting("webhooks")]
public class WebhooksController(ISender mediator, IOptions<WebhookOptions> options, ILogger<WebhooksController> logger)
    : ControllerBase
{
    [HttpPost("auth")]
    public async Task<IActionResult> Auth(MediaMtxAuthRequest request, CancellationToken ct)
    {
        // Reads (HLS preview clients, and the compositor's own internal RTSP pull, and
        // MediaMTX's own control-API/metrics actions) are never gated by stream key - only the
        // internal Docker network can reach this endpoint at all.
        if (!string.Equals(request.Action, "publish", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "MediaMTX auth: action={Action} path={Path} ip={Ip} protocol={Protocol} result=allow (non-publish action)",
                request.Action, MaskPath(request.Path), request.Ip, request.Protocol);
            return Ok();
        }

        var streamKey = ExtractStreamKey(request.Path);
        var allowed = !string.IsNullOrEmpty(streamKey) && await mediator.Send(new HandlePublishWebhookCommand(streamKey), ct);

        logger.LogInformation(
            "MediaMTX auth: action=publish path={Path} ip={Ip} protocol={Protocol} result={Result}",
            MaskPath(request.Path), request.Ip, request.Protocol, allowed ? "allow" : "deny");

        return allowed ? Ok() : Unauthorized();
    }

    [HttpPost("ready")]
    public async Task<IActionResult> Ready(MediaMtxNotificationRequest request, CancellationToken ct)
    {
        if (!IsAuthorizedByWebhookSecret())
        {
            logger.LogWarning("MediaMTX ready webhook rejected: missing or invalid X-Webhook-Secret");
            return Unauthorized();
        }

        var streamKey = ExtractStreamKey(request.Path);
        logger.LogInformation("MediaMTX ready: path={Path}", MaskPath(request.Path));
        await mediator.Send(new HandleReadyWebhookCommand(streamKey), ct);
        return Ok();
    }

    [HttpPost("not-ready")]
    public async Task<IActionResult> NotReady(MediaMtxNotificationRequest request, CancellationToken ct)
    {
        if (!IsAuthorizedByWebhookSecret())
        {
            logger.LogWarning("MediaMTX not-ready webhook rejected: missing or invalid X-Webhook-Secret");
            return Unauthorized();
        }

        var streamKey = ExtractStreamKey(request.Path);
        logger.LogInformation("MediaMTX not-ready: path={Path}", MaskPath(request.Path));
        await mediator.Send(new HandleNotReadyWebhookCommand(streamKey), ct);
        return Ok();
    }

    private bool IsAuthorizedByWebhookSecret() =>
        !string.IsNullOrEmpty(options.Value.Secret) &&
        Request.Headers.TryGetValue("X-Webhook-Secret", out var provided) &&
        provided == options.Value.Secret;

    /// <summary>MediaMTX path names look like "live/{streamKey}" (see deploy/mediamtx/mediamtx.yml PathPrefix).</summary>
    internal static string ExtractStreamKey(string path) =>
        string.IsNullOrEmpty(path) ? string.Empty : path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Never log the raw stream key - only its length and a short prefix, enough to correlate log lines without leaking the credential.</summary>
    private static string MaskPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "(empty)";
        }

        var slashIndex = path.LastIndexOf('/');
        var prefix = slashIndex >= 0 ? path[..(slashIndex + 1)] : string.Empty;
        var key = ExtractStreamKey(path);

        return key.Length <= 4
            ? $"{prefix}****"
            : $"{prefix}{key[..4]}…({key.Length} chars)";
    }
}
