using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Channels.Commands;

public sealed record UpdateChannelCommand(
    Guid ChannelId,
    string Name,
    bool IsActive,
    SceneThresholds Thresholds) : IRequest<ChannelDto>;

public sealed class UpdateChannelCommandHandler(IChannelRepository channels, IApplicationDbContext db, IDateTimeProvider clock)
    : IRequestHandler<UpdateChannelCommand, ChannelDto>
{
    public async Task<ChannelDto> Handle(UpdateChannelCommand request, CancellationToken ct)
    {
        var channel = await channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException(nameof(Channel), request.ChannelId);

        channel.Name = request.Name;
        channel.IsActive = request.IsActive;
        channel.SceneThresholds = request.Thresholds;
        channel.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return ChannelDto.FromEntity(channel);
    }
}
