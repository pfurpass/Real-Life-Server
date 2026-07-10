using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RealLifeServer.Application.Streams.Commands;

namespace RealLifeServer.Api.Controllers;

/// <summary>Bound from configuration section "Webhooks". The shared secret MediaMTX sends back on every callback (see deploy/mediamtx/mediamtx.yml).</summary>
public class WebhookOptions
{
    public string Secret { get; set; } = string.Empty;
}

public sealed record MediaMtxAuthRequest(string Path, string Action);
public sealed record MediaMtxNotificationRequest(string Path);

/// <summary>
/// Endpoints called by MediaMTX, not by the frontend: the synchronous auth gate
/// (`authHTTPAddress`) and the fire-and-forget ready/not-ready notification hooks. See
/// docs/CONCEPT.md, chapter 3 and 10. Secured by a shared secret header, not JWT - MediaMTX has
/// no user session - and reachable only from the internal Docker network in production.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks/mediamtx")]
[EnableRateLimiting("webhooks")]
public class WebhooksController(ISender mediator, IOptions<WebhookOptions> options) : ControllerBase
{
    [HttpPost("auth")]
    public async Task<IActionResult> Auth(MediaMtxAuthRequest request, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        // Reads (HLS preview clients, and the compositor's own internal RTSP pull) are never
        // gated by stream key - only the internal Docker network can reach MediaMTX at all.
        if (!string.Equals(request.Action, "publish", StringComparison.OrdinalIgnoreCase))
        {
            return Ok();
        }

        var streamKey = ExtractStreamKey(request.Path);
        var allowed = await mediator.Send(new HandlePublishWebhookCommand(streamKey), ct);
        return allowed ? Ok() : Unauthorized();
    }

    [HttpPost("ready")]
    public async Task<IActionResult> Ready(MediaMtxNotificationRequest request, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        await mediator.Send(new HandleReadyWebhookCommand(ExtractStreamKey(request.Path)), ct);
        return Ok();
    }

    [HttpPost("not-ready")]
    public async Task<IActionResult> NotReady(MediaMtxNotificationRequest request, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        await mediator.Send(new HandleNotReadyWebhookCommand(ExtractStreamKey(request.Path)), ct);
        return Ok();
    }

    private bool IsAuthorized() =>
        !string.IsNullOrEmpty(options.Value.Secret) &&
        Request.Headers.TryGetValue("X-Webhook-Secret", out var provided) &&
        provided == options.Value.Secret;

    /// <summary>MediaMTX path names look like "live/{streamKey}" (see deploy/mediamtx/mediamtx.yml PathPrefix).</summary>
    private static string ExtractStreamKey(string path) => path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
}
