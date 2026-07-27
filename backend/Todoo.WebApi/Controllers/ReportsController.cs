using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todoo.Business.Abstract;
using Todoo.WebApi.Helpers;

namespace Todoo.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly ISlaPerformanceService _slaPerformanceService;

    public ReportsController(IReportService reportService, ISlaPerformanceService slaPerformanceService)
    {
        _reportService = reportService;
        _slaPerformanceService = slaPerformanceService;
    }

    [HttpGet("task-summary")]
    public async Task<IActionResult> GetTaskSummary([FromQuery] int teamId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        if (teamId <= 0)
        {
            return BadRequest(new { success = false, message = "teamId parametresi zorunludur ve 0'dan buyuk olmalidir." });
        }

        var result = await _reportService.GetTaskReportByTeamIdAsync(teamId, userId);
        return result.ToActionResult();
    }

    [HttpGet("sla/me")]
    public async Task<IActionResult> GetMySla([FromQuery] int teamId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        if (teamId <= 0)
        {
            return BadRequest(new { success = false, message = "teamId parametresi zorunludur ve 0'dan buyuk olmalidir." });
        }

        var result = await _slaPerformanceService.GetMyPerformanceAsync(teamId, userId);
        return result.ToActionResult();
    }

    [HttpGet("sla/members")]
    public async Task<IActionResult> GetTeamMembersSla([FromQuery] int teamId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { success = false, message = "Gecerli bir kullanici bilgisi bulunamadi." });
        }

        if (teamId <= 0)
        {
            return BadRequest(new { success = false, message = "teamId parametresi zorunludur ve 0'dan buyuk olmalidir." });
        }

        var result = await _slaPerformanceService.GetTeamMembersPerformanceAsync(teamId, userId);
        return result.ToActionResult();
    }
}
