using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RealLifeServer.Api.Controllers;
using RealLifeServer.Application.Streams.Commands;
using Xunit;

namespace RealLifeServer.Api.Tests;

/// <summary>
/// Covers the MediaMTX auth-flow fix (docs/CONCEPT.md chapter 3/10): the root cause of the
/// reported "server replied with code 401" was that /auth required a X-Webhook-Secret header,
/// but MediaMTX's authHTTPAddress mechanism sends a fixed payload with no configurable headers
/// - it can never satisfy that check. /auth must instead validate the real stream key; only
/// /ready and /not-ready (which we invoke ourselves via runOnReady/runOnNotReady shell hooks)
/// can and do carry the shared secret.
/// </summary>
public class WebhooksControllerTests
{
    private const string CorrectSecret = "the-configured-webhook-secret";

    private static WebhooksController CreateController(ISender mediator, string? secret = CorrectSecret)
    {
        var options = Options.Create(new WebhookOptions { Secret = secret ?? string.Empty });
        return new WebhooksController(mediator, options, NullLogger<WebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task Auth_ValidPublishKey_ReturnsOk_WithoutRequiringAnySecretHeader()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<HandlePublishWebhookCommand>(), Arg.Any<CancellationToken>()).Returns(true);

        // No X-Webhook-Secret header is set on this request at all - matching what MediaMTX's
        // authHTTPAddress actually sends. This must still succeed.
        var controller = CreateController(mediator);

        var result = await controller.Auth(new MediaMtxAuthRequest("live/valid-stream-key", "publish", "172.25.89.238", "rtmp"), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await mediator.Received(1).Send(Arg.Is<HandlePublishWebhookCommand>(c => c.StreamKey == "valid-stream-key"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auth_InvalidPublishKey_ReturnsUnauthorized()
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<HandlePublishWebhookCommand>(), Arg.Any<CancellationToken>()).Returns(false);
        var controller = CreateController(mediator);

        var result = await controller.Auth(new MediaMtxAuthRequest("live/unknown-key", "publish", "1.2.3.4", "rtmp"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Auth_EmptyPath_ReturnsUnauthorized_AndDoesNotDispatchACommand()
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator);

        var result = await controller.Auth(new MediaMtxAuthRequest(string.Empty, "publish", "1.2.3.4", "rtmp"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        await mediator.DidNotReceive().Send(Arg.Any<HandlePublishWebhookCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("read")]
    [InlineData("api")]
    [InlineData("metrics")]
    public async Task Auth_NonPublishAction_ReturnsOk_WithoutCheckingStreamKey(string action)
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator);

        var result = await controller.Auth(new MediaMtxAuthRequest("live/whatever", action, "1.2.3.4", "rtsp"), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await mediator.DidNotReceive().Send(Arg.Any<HandlePublishWebhookCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ready_WithoutSecretHeader_ReturnsUnauthorized_AndDoesNotDispatchACommand()
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator);
        // Deliberately not setting the X-Webhook-Secret header.

        var result = await controller.Ready(new MediaMtxNotificationRequest("live/some-key"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        await mediator.DidNotReceive().Send(Arg.Any<HandleReadyWebhookCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ready_WithWrongSecretHeader_ReturnsUnauthorized()
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator);
        controller.Request.Headers["X-Webhook-Secret"] = "wrong-secret";

        var result = await controller.Ready(new MediaMtxNotificationRequest("live/some-key"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Ready_WithCorrectSecretHeader_ReturnsOk_AndDispatchesTheCommand()
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator);
        controller.Request.Headers["X-Webhook-Secret"] = CorrectSecret;

        var result = await controller.Ready(new MediaMtxNotificationRequest("live/some-key"), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await mediator.Received(1).Send(Arg.Is<HandleReadyWebhookCommand>(c => c.StreamKey == "some-key"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotReady_WithoutSecretHeader_ReturnsUnauthorized()
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator);

        var result = await controller.NotReady(new MediaMtxNotificationRequest("live/some-key"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        await mediator.DidNotReceive().Send(Arg.Any<HandleNotReadyWebhookCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotReady_WithCorrectSecretHeader_ReturnsOk()
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator);
        controller.Request.Headers["X-Webhook-Secret"] = CorrectSecret;

        var result = await controller.NotReady(new MediaMtxNotificationRequest("live/some-key"), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await mediator.Received(1).Send(Arg.Is<HandleNotReadyWebhookCommand>(c => c.StreamKey == "some-key"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("live/abc123", "abc123")]
    [InlineData("abc123", "abc123")]
    [InlineData("live/nested/abc123", "abc123")]
    [InlineData("", "")]
    public void ExtractStreamKey_ParsesThePathCorrectly(string path, string expected)
    {
        Assert.Equal(expected, WebhooksController.ExtractStreamKey(path));
    }
}
