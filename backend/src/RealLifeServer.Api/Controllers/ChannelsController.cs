using MediatR;
using Microsoft.AspNetCore.Mvc;
using RealLifeServer.Application.Channels.Commands;
using RealLifeServer.Application.Channels.Queries;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Api.Controllers;

public sealed record CreateChannelRequest(string Name, IngestProtocol IngestProtocols);
public sealed record UpdateChannelRequest(string Name, bool IsActive, SceneThresholds Thresholds);
public sealed record AddDestinationRequest(StreamPlatform Platform, string RtmpUrl, string StreamKey);
public sealed record SetDestinationEnabledRequest(bool IsEnabled);
public sealed record ReplaceDestinationKeyRequest(string StreamKey);
public sealed record ReorderDestinationsRequest(IReadOnlyList<Guid> OrderedDestinationIds);

[Route("api/channels")]
public class ChannelsController(ISender mediator, ICurrentUserService currentUser, IChannelRepository channels)
    : ApiControllerBase(mediator, currentUser)
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetChannelsQuery(CurrentUser.UserId!.Value, CurrentUser.Role!.Value), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        return Ok(await Mediator.Send(new GetChannelByIdQuery(id), ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateChannelRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateChannelCommand(CurrentUser.UserId!.Value, request.Name, request.IngestProtocols), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateChannelRequest request, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        return Ok(await Mediator.Send(new UpdateChannelCommand(id, request.Name, request.IsActive, request.Thresholds), ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        await Mediator.Send(new DeleteChannelCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/regenerate-key")]
    public async Task<IActionResult> RegenerateKey(Guid id, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        return Ok(await Mediator.Send(new RegenerateStreamKeyCommand(id), ct));
    }

    [HttpPost("{id:guid}/destinations")]
    public async Task<IActionResult> AddDestination(Guid id, AddDestinationRequest request, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        return Ok(await Mediator.Send(new AddStreamDestinationCommand(id, request.Platform, request.RtmpUrl, request.StreamKey), ct));
    }

    [HttpDelete("{id:guid}/destinations/{destinationId:guid}")]
    public async Task<IActionResult> RemoveDestination(Guid id, Guid destinationId, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        await Mediator.Send(new RemoveStreamDestinationCommand(id, destinationId), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/destinations/{destinationId:guid}/enabled")]
    public async Task<IActionResult> SetDestinationEnabled(Guid id, Guid destinationId, SetDestinationEnabledRequest request, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        return Ok(await Mediator.Send(new SetStreamDestinationEnabledCommand(id, destinationId, request.IsEnabled), ct));
    }

    [HttpPost("{id:guid}/destinations/{destinationId:guid}/stream-key")]
    public async Task<IActionResult> ReplaceDestinationKey(Guid id, Guid destinationId, ReplaceDestinationKeyRequest request, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        return Ok(await Mediator.Send(new ReplaceStreamDestinationKeyCommand(id, destinationId, request.StreamKey), ct));
    }

    [HttpPut("{id:guid}/destinations/order")]
    public async Task<IActionResult> ReorderDestinations(Guid id, ReorderDestinationsRequest request, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(id, channels, ct);
        return Ok(await Mediator.Send(new ReorderStreamDestinationsCommand(id, request.OrderedDestinationIds), ct));
    }
}
