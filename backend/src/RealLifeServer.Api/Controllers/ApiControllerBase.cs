using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public abstract class ApiControllerBase(ISender mediator, ICurrentUserService currentUser) : ControllerBase
{
    protected ISender Mediator { get; } = mediator;
    protected ICurrentUserService CurrentUser { get; } = currentUser;

    /// <summary>
    /// Streamers may only act on channels they own; Admin/Moderator may act on any channel.
    /// See docs/CONCEPT.md, chapter 10 (roles).
    /// </summary>
    protected async Task EnsureChannelAccessAsync(Guid channelId, IChannelRepository channels, CancellationToken ct)
    {
        if (CurrentUser.Role is UserRole.Admin or UserRole.Moderator)
        {
            return;
        }

        var channel = await channels.GetByIdAsync(channelId, ct) ?? throw new NotFoundException(nameof(Channel), channelId);
        if (channel.OwnerUserId != CurrentUser.UserId)
        {
            throw new ForbiddenAccessException("You do not have access to this channel.");
        }
    }
}
