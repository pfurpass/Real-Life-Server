using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>Wraps the authenticated user's claims for the current HTTP request.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    UserRole? Role { get; }
    bool IsAuthenticated { get; }
}
