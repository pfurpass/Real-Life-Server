using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Application.Common.Interfaces;

public interface IJwtTokenService
{
    /// <summary>Short-lived signed JWT access token (default 15 min).</summary>
    string GenerateAccessToken(User user);

    /// <summary>Opaque, cryptographically random refresh token. The caller is responsible for hashing it before persisting.</summary>
    string GenerateRefreshToken();
}
