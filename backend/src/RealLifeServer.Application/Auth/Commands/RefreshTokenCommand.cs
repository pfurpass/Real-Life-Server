using MediatR;
using RealLifeServer.Application.Auth.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Common.Utils;
using RealLifeServer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace RealLifeServer.Application.Auth.Commands;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResultDto>;

public sealed class RefreshTokenCommandHandler(
    IApplicationDbContext db,
    IUserRepository users,
    IJwtTokenService jwtTokenService,
    IDateTimeProvider clock)
    : IRequestHandler<RefreshTokenCommand, AuthResultDto>
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    public async Task<AuthResultDto> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive)
        {
            throw new ValidationException("Refresh token is invalid or expired.");
        }

        var user = await users.GetByIdAsync(stored.UserId, ct)
            ?? throw new NotFoundException(nameof(User), stored.UserId);

        // Rotate: revoke the old token and issue a fresh pair. Prevents replay of a stolen token.
        var now = clock.UtcNow;
        stored.RevokedAt = now;

        var newRefreshToken = jwtTokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(newRefreshToken),
            ExpiresAt = now.Add(RefreshTokenLifetime),
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);

        var accessToken = jwtTokenService.GenerateAccessToken(user);
        return new AuthResultDto(user.Id, user.Username, user.Role, accessToken, newRefreshToken, now.Add(AccessTokenLifetime));
    }
}
