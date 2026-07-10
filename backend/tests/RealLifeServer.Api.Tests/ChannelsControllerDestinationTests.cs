using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using RealLifeServer.Api.Controllers;
using RealLifeServer.Application.Channels.Commands;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;
using Xunit;

namespace RealLifeServer.Api.Tests;

/// <summary>
/// A Streamer must only be able to manage destinations on channels they own; Admin/Moderator may
/// act on any channel (ApiControllerBase.EnsureChannelAccessAsync, docs/CONCEPT.md chapter 10).
/// Covers every new destination-management endpoint.
/// </summary>
public class ChannelsControllerDestinationTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ChannelId = Guid.NewGuid();

    private static ChannelsController CreateController(ISender mediator, IChannelRepository channels, Guid userId, UserRole role)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId);
        currentUser.Role.Returns(role);
        currentUser.IsAuthenticated.Returns(true);

        return new ChannelsController(mediator, currentUser, channels)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static IChannelRepository CreateChannelsRepository()
    {
        var channel = new Channel { Id = ChannelId, OwnerUserId = OwnerId, Name = "Test", StreamKey = "key" };
        var channels = Substitute.For<IChannelRepository>();
        channels.GetByIdAsync(ChannelId, Arg.Any<CancellationToken>()).Returns(channel);
        return channels;
    }

    public static IEnumerable<object[]> DestinationEndpointCalls()
    {
        yield return
        [
            (Func<ChannelsController, Task<IActionResult>>)(c => c.AddDestination(ChannelId, new AddDestinationRequest(StreamPlatform.Twitch, "rtmp://live.twitch.tv/app", "key"), CancellationToken.None))
        ];
        yield return
        [
            (Func<ChannelsController, Task<IActionResult>>)(c => c.RemoveDestination(ChannelId, Guid.NewGuid(), CancellationToken.None))
        ];
        yield return
        [
            (Func<ChannelsController, Task<IActionResult>>)(c => c.SetDestinationEnabled(ChannelId, Guid.NewGuid(), new SetDestinationEnabledRequest(false), CancellationToken.None))
        ];
        yield return
        [
            (Func<ChannelsController, Task<IActionResult>>)(c => c.ReplaceDestinationKey(ChannelId, Guid.NewGuid(), new ReplaceDestinationKeyRequest("new-key"), CancellationToken.None))
        ];
        yield return
        [
            (Func<ChannelsController, Task<IActionResult>>)(c => c.ReorderDestinations(ChannelId, new ReorderDestinationsRequest([]), CancellationToken.None))
        ];
    }

    [Theory]
    [MemberData(nameof(DestinationEndpointCalls))]
    public async Task NonOwningStreamer_IsForbidden(Func<ChannelsController, Task<IActionResult>> call)
    {
        var mediator = Substitute.For<ISender>();
        var controller = CreateController(mediator, CreateChannelsRepository(), OtherUserId, UserRole.Streamer);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => call(controller));
        Assert.Empty(mediator.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(DestinationEndpointCalls))]
    public async Task OwningStreamer_IsAllowedThrough(Func<ChannelsController, Task<IActionResult>> call)
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<IRequest<ChannelDto>>(), Arg.Any<CancellationToken>())
            .Returns(new ChannelDto(ChannelId, "Test", "test", "key", IngestProtocol.Rtmp, true, SceneState.Offline, new SceneThresholds(), CompositorStrategy.Zmq, [], DateTimeOffset.UtcNow));

        var controller = CreateController(mediator, CreateChannelsRepository(), OwnerId, UserRole.Streamer);

        var result = await call(controller);

        Assert.NotNull(result);
        Assert.Single(mediator.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(DestinationEndpointCalls))]
    public async Task Admin_IsAllowedThroughEvenWithoutOwningTheChannel(Func<ChannelsController, Task<IActionResult>> call)
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<IRequest<ChannelDto>>(), Arg.Any<CancellationToken>())
            .Returns(new ChannelDto(ChannelId, "Test", "test", "key", IngestProtocol.Rtmp, true, SceneState.Offline, new SceneThresholds(), CompositorStrategy.Zmq, [], DateTimeOffset.UtcNow));

        var controller = CreateController(mediator, CreateChannelsRepository(), OtherUserId, UserRole.Admin);

        var result = await call(controller);

        Assert.NotNull(result);
        Assert.Single(mediator.ReceivedCalls());
    }
}
