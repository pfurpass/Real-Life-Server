using MediatR;
using RealLifeServer.Application.Channels.Dtos;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Channels.Queries;

/// <summary>Admins/Moderators see every channel; Streamers only see their own.</summary>
public sealed record GetChannelsQuery(Guid RequestingUserId, UserRole RequestingUserRole) : IRequest<IReadOnlyList<ChannelDto>>;

public sealed class GetChannelsQueryHandler(IChannelRepository channels)
    : IRequestHandler<GetChannelsQuery, IReadOnlyList<ChannelDto>>
{
    public async Task<IReadOnlyList<ChannelDto>> Handle(GetChannelsQuery request, CancellationToken ct)
    {
        var result = request.RequestingUserRole is UserRole.Admin or UserRole.Moderator
            ? await channels.GetAllAsync(ct)
            : await channels.GetForOwnerAsync(request.RequestingUserId, ct);

        return result.Select(ChannelDto.FromEntity).ToList();
    }
}
