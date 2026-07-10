using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Api.Controllers;

[ApiController]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Moderator)}")]
[Route("api/system")]
public class SystemController(ISystemStatsProvider statsProvider) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct) => Ok(await statsProvider.GetCurrentStatsAsync(ct));
}
