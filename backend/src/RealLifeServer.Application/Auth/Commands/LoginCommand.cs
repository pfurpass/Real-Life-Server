using MediatR;
using RealLifeServer.Application.Auth.Dtos;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Common.Utils;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Auth.Commands;

public sealed record LoginCommand(string Username, string Password) : IRequest<AuthResultDto>;

public sealed class LoginCommandHandler(
    IUserRepository users,
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IDateTimeProvider clock)
    : IRequestHandler<LoginCommand, AuthResultDto>
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(request.Username, ct);
        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new ValidationException("Invalid username or password.");
        }

        var accessToken = jwtTokenService.GenerateAccessToken(user);
        var refreshToken = jwtTokenService.GenerateRefreshToken();
        var now = clock.UtcNow;

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(refreshToken),
            ExpiresAt = now.Add(RefreshTokenLifetime),
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);

        return new AuthResultDto(user.Id, user.Username, user.Role, accessToken, refreshToken, now.Add(AccessTokenLifetime));
    }
}
