using MediatR;
using Microsoft.AspNetCore.Mvc;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.Commands;
using RealLifeServer.Application.Streams.Queries;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Api.Controllers;

public sealed record ForceSceneRequest(SceneState TargetState);
public sealed record HeartbeatRequest(string Source = "encoder-agent");

[Route("api/streams")]
public class StreamsController(ISender mediator, ICurrentUserService currentUser, IChannelRepository channels)
    : ApiControllerBase(mediator, currentUser)
{
    [HttpGet("{channelId:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid channelId, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(channelId, channels, ct);
        return Ok(await Mediator.Send(new GetStreamStatusQuery(channelId), ct));
    }

    [HttpGet("{channelId:guid}/metrics")]
    public async Task<IActionResult> GetMetrics(Guid channelId, [FromQuery] int rangeMinutes, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(channelId, channels, ct);
        var range = TimeSpan.FromMinutes(rangeMinutes <= 0 ? 60 : rangeMinutes);
        return Ok(await Mediator.Send(new GetMetricsHistoryQuery(channelId, range), ct));
    }

    [HttpGet("{channelId:guid}/events")]
    public async Task<IActionResult> GetEvents(Guid channelId, [FromQuery] int take, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(channelId, channels, ct);
        return Ok(await Mediator.Send(new GetSceneEventsQuery(channelId, take <= 0 ? 100 : take), ct));
    }

    [HttpPost("{channelId:guid}/force-scene")]
    public async Task<IActionResult> ForceScene(Guid channelId, ForceSceneRequest request, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(channelId, channels, ct);
        await Mediator.Send(new ForceSceneCommand(channelId, request.TargetState), ct);
        return NoContent();
    }

    [HttpPost("{channelId:guid}/stop")]
    public async Task<IActionResult> Stop(Guid channelId, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(channelId, channels, ct);
        await Mediator.Send(new StopChannelCommand(channelId), ct);
        return NoContent();
    }

    [HttpPost("{channelId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid channelId, HeartbeatRequest request, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(channelId, channels, ct);
        await Mediator.Send(new RecordHeartbeatCommand(channelId, request.Source), ct);
        return NoContent();
    }
}
