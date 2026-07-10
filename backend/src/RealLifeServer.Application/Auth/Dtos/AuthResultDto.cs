using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Auth.Dtos;

public sealed record AuthResultDto(
    Guid UserId,
    string Username,
    UserRole Role,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);
