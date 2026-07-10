using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RealLifeServer.Application.Auth.Commands;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Api.Controllers;

public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record RegisterRequest(string Username, string Email, string Password, UserRole Role);

[ApiController]
[Route("api/auth")]
public class AuthController(ISender mediator) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new LoginCommand(request.Username, request.Password), ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        return Ok(result);
    }

    [HttpPost("register")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var userId = await mediator.Send(new RegisterUserCommand(request.Username, request.Email, request.Password, request.Role), ct);
        return CreatedAtAction(nameof(Register), new { id = userId }, new { id = userId });
    }
}
