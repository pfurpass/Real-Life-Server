using MediatR;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Auth.Commands;

/// <summary>Admin-only: creates a new user account. See docs/CONCEPT.md, chapter 9 (REST API).</summary>
public sealed record RegisterUserCommand(string Username, string Email, string Password, UserRole Role) : IRequest<Guid>;

public sealed class RegisterUserCommandHandler(IUserRepository users, IApplicationDbContext db, IPasswordHasher passwordHasher)
    : IRequestHandler<RegisterUserCommand, Guid>
{
    public async Task<Guid> Handle(RegisterUserCommand request, CancellationToken ct)
    {
        if (await users.GetByUsernameAsync(request.Username, ct) is not null)
        {
            throw new ValidationException($"Username \"{request.Username}\" is already taken.");
        }
        if (await users.GetByEmailAsync(request.Email, ct) is not null)
        {
            throw new ValidationException($"Email \"{request.Email}\" is already registered.");
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = request.Role
        };

        users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }
}
